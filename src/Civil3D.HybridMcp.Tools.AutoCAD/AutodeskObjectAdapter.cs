using System.Globalization;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Civil3D.HybridMcp.Protocol;
using AcException = Autodesk.AutoCAD.Runtime.Exception;

namespace Civil3D.HybridMcp.Tools.AutoCAD;

/// <summary>
/// Generic object reads for any <see cref="DBObject"/> in the drawing.
///
/// Identity comes from the runtime class, so a Civil 3D alignment and an AEC
/// object report through the same shape without this module referencing either
/// API. Property enumeration is reflection over the concrete .NET type rather
/// than a hardcoded list, which is the whole point: the tool exists for classes
/// the platform has no typed reader for.
///
/// Everything runs on an object opened <see cref="OpenMode.ForRead"/>. That is
/// not only a convention here: AutoCAD itself rejects a getter that tries to
/// mutate a read-only object, so the open mode is the outer guard and the
/// per-property try/catch is the inner one.
/// </summary>
public sealed class AutodeskObjectAdapter : IObjectReadAdapter
{
    /// <summary>Upper bound on emitted properties, so a pathological class cannot stall the host.</summary>
    private const int MaxProperties = 500;

    /// <summary>Depth guard for the runtime class walk.</summary>
    private const int MaxClassDepth = 24;

    /// <summary>
    /// Properties skipped before they are ever invoked. These build COM wrappers
    /// or unmanaged helpers rather than returning drawing data, and none of them
    /// answers a question a caller would ask about the object.
    /// </summary>
    private static readonly HashSet<string> SkippedProperties = new(StringComparer.Ordinal)
    {
        "AcadObject",
        "Drawable",
        "UndoFiler",
        "MaterialMapper",
        "CloneMeForDragging",
        "CompoundObjectTransform"
    };

    private readonly Document _document;
    private readonly DocumentDescriptor _descriptor;

    public AutodeskObjectAdapter(Document document, DocumentDescriptor descriptor)
    {
        _document = document;
        _descriptor = descriptor;
    }

    public CadObjectInfo? GetObject(string handle)
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();

        var target = OpenForRead(database, transaction, handle);
        if (target is null)
        {
            transaction.Commit();
            return null;
        }

        var rxClass = Safe(target.GetRXClass);
        var info = new CadObjectInfo
        {
            Handle = Safe(() => target.Handle.ToString()) ?? handle,
            ObjectId = Safe(() => target.ObjectId.ToString()),
            ObjectClass = Safe(() => rxClass?.Name) ?? target.GetType().Name,
            DxfName = Safe(() => rxClass?.DxfName),
            AppName = Safe(() => rxClass?.AppName),
            ClassHierarchy = ReadClassHierarchy(rxClass),
            IsEntity = target is Entity,
            IsErased = Safe(() => target.IsErased),
            IsProxy = Safe(() => target.IsAProxy),
            IsPersistent = Safe(() => target.IsPersistent),
            OwnerHandle = ReadOwnerHandle(transaction, target),
            OwnerClass = ReadOwnerClass(transaction, target),
            ObjectReference = BuildReference(target, rxClass),
            Entity = target is Entity entity ? ReadEntityInfo(entity) : null
        };

        transaction.Commit();
        return info;
    }

    public ObjectPropertyReadResult? ReadProperties(string handle)
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();

        var target = OpenForRead(database, transaction, handle);
        if (target is null)
        {
            transaction.Commit();
            return null;
        }

        var objectClass = Safe(() => target.GetRXClass()?.Name) ?? target.GetType().Name;
        var candidates = target.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => !SkippedProperties.Contains(property.Name))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

        var properties = new List<ObjectPropertyInfo>();
        var truncated = false;

        foreach (var property in candidates)
        {
            if (properties.Count >= MaxProperties)
            {
                truncated = true;
                break;
            }

            var described = Describe(target, property);
            if (described is not null)
            {
                properties.Add(described);
            }
        }

        transaction.Commit();
        return new ObjectPropertyReadResult(objectClass, properties, truncated);
    }

    /// <summary>
    /// Reads one property. A getter that throws produces a row carrying the
    /// reason rather than a missing row or an invented value, because "the host
    /// refused to evaluate this" is itself the answer the caller needs.
    /// Returns null only when the value type has no honest scalar form.
    /// </summary>
    private static ObjectPropertyInfo? Describe(DBObject target, PropertyInfo property)
    {
        object? raw;
        try
        {
            raw = property.GetValue(target);
        }
        catch (TargetInvocationException ex)
        {
            return Unreadable(property, DescribeException(ex.InnerException ?? ex));
        }
        catch (System.Exception ex)
        {
            return Unreadable(property, DescribeException(ex));
        }

        if (raw is null)
        {
            return new ObjectPropertyInfo
            {
                Name = property.Name,
                DeclaringType = property.DeclaringType?.Name,
                DataType = property.PropertyType.Name,
                Value = null,
                NumericValue = null,
                IsReadOnly = !property.CanWrite
            };
        }

        if (!TryFormat(raw, out var value, out var numeric))
        {
            // A structured value with no honest scalar form is left out entirely
            // rather than stringified into something the caller cannot parse.
            return null;
        }

        return new ObjectPropertyInfo
        {
            Name = property.Name,
            DeclaringType = property.DeclaringType?.Name,
            DataType = property.PropertyType.Name,
            Value = value,
            NumericValue = numeric,
            IsReadOnly = !property.CanWrite
        };
    }

    private static ObjectPropertyInfo Unreadable(PropertyInfo property, string reason) =>
        new()
        {
            Name = property.Name,
            DeclaringType = property.DeclaringType?.Name,
            DataType = property.PropertyType.Name,
            Value = null,
            NumericValue = null,
            IsReadOnly = !property.CanWrite,
            Error = reason
        };

    /// <summary>
    /// Formats a value invariantly. Culture matters here: this host runs in
    /// pt-BR, where a default <c>ToString</c> would emit a decimal comma and
    /// break every caller that parses the number back.
    /// </summary>
    private static bool TryFormat(object raw, out string? value, out double? numeric)
    {
        value = null;
        numeric = null;

        switch (raw)
        {
            case string text:
                value = text;
                return true;
            case bool flag:
                value = flag ? "true" : "false";
                return true;
            case double number:
                value = number.ToString("R", CultureInfo.InvariantCulture);
                numeric = number;
                return true;
            case float number:
                value = number.ToString("R", CultureInfo.InvariantCulture);
                numeric = number;
                return true;
            case decimal number:
                value = number.ToString(CultureInfo.InvariantCulture);
                numeric = (double)number;
                return true;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                value = Convert.ToString(raw, CultureInfo.InvariantCulture);
                numeric = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                return true;
            case Enum enumeration:
                value = enumeration.ToString();
                numeric = Safe(() => (double?)Convert.ToDouble(
                    Convert.ChangeType(enumeration, Enum.GetUnderlyingType(enumeration.GetType()), CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture));
                return true;
            case DateTime timestamp:
                value = timestamp.ToString("O", CultureInfo.InvariantCulture);
                return true;
            case TimeSpan span:
                value = span.ToString("c", CultureInfo.InvariantCulture);
                return true;
            case Point3d point:
                value = FormatOrdinates(point.X, point.Y, point.Z);
                return true;
            case Point2d point:
                value = FormatOrdinates(point.X, point.Y);
                return true;
            case Vector3d vector:
                value = FormatOrdinates(vector.X, vector.Y, vector.Z);
                return true;
            case Vector2d vector:
                value = FormatOrdinates(vector.X, vector.Y);
                return true;
            case ObjectId id:
                value = id.ToString();
                return true;
            case Handle objectHandle:
                value = objectHandle.ToString();
                return true;
            default:
                return false;
        }
    }

    private static string FormatOrdinates(params double[] ordinates) =>
        string.Join(",", ordinates.Select(ordinate => ordinate.ToString("R", CultureInfo.InvariantCulture)));

    private static CadEntityInfo ReadEntityInfo(Entity entity) =>
        new()
        {
            Layer = Safe(() => entity.Layer),
            Linetype = Safe(() => entity.Linetype),
            LinetypeScale = Safe(() => (double?)entity.LinetypeScale),
            Lineweight = Safe(() => entity.LineWeight.ToString()),
            ColorIndex = Safe(() => (int?)entity.ColorIndex),
            Color = Safe(() => entity.Color.ColorMethod.ToString()),
            TransparencyAlpha = Safe(() => (int?)entity.Transparency.Alpha),
            IsVisible = Safe(() => (bool?)entity.Visible),
            BlockName = Safe(() => entity.BlockName),
            Extents = ReadExtents(entity)
        };

    /// <summary>
    /// Extents are optional by nature: an empty block reference or a degenerate
    /// entity throws <c>eNullExtents</c> instead of returning a box.
    /// </summary>
    private static CadExtentsInfo? ReadExtents(Entity entity)
    {
        try
        {
            var extents = entity.GeometricExtents;
            return new CadExtentsInfo
            {
                MinX = extents.MinPoint.X,
                MinY = extents.MinPoint.Y,
                MinZ = extents.MinPoint.Z,
                MaxX = extents.MaxPoint.X,
                MaxY = extents.MaxPoint.Y,
                MaxZ = extents.MaxPoint.Z
            };
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadClassHierarchy(RXClass? rxClass)
    {
        var hierarchy = new List<string>();
        var current = Safe(() => rxClass?.MyParent);

        while (current is not null && hierarchy.Count < MaxClassDepth)
        {
            var name = Safe(() => current.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                break;
            }

            hierarchy.Add(name);
            current = Safe(() => current.MyParent);
        }

        return hierarchy;
    }

    private static string? ReadOwnerHandle(Transaction transaction, DBObject target) =>
        Safe(() => OpenOwner(transaction, target)?.Handle.ToString());

    private static string? ReadOwnerClass(Transaction transaction, DBObject target) =>
        Safe(() => OpenOwner(transaction, target)?.GetRXClass()?.Name);

    private static DBObject? OpenOwner(Transaction transaction, DBObject target)
    {
        var ownerId = target.OwnerId;
        return ownerId.IsNull
            ? null
            : transaction.GetObject(ownerId, OpenMode.ForRead, openErased: false);
    }

    private CadObjectReference BuildReference(DBObject target, RXClass? rxClass) =>
        new()
        {
            InstanceId = _descriptor.InstanceId,
            DocumentId = _descriptor.DocumentId,
            DatabaseFingerprint = _descriptor.DatabaseFingerprint,
            Handle = Safe(() => target.Handle.ToString()),
            ObjectId = Safe(() => target.ObjectId.ToString()),
            Type = Safe(() => rxClass?.Name) ?? target.GetType().Name,
            Name = null
        };

    private static DBObject? OpenForRead(Database database, Transaction transaction, string handleText)
    {
        if (!TryGetObjectId(database, handleText, out var objectId) || objectId.IsNull || objectId.IsErased)
        {
            return null;
        }

        try
        {
            return transaction.GetObject(objectId, OpenMode.ForRead, openErased: false);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetObjectId(Database database, string handleText, out ObjectId objectId)
    {
        objectId = ObjectId.Null;
        try
        {
            var value = Convert.ToInt64(handleText, 16);
            objectId = database.GetObjectId(createIfNotFound: false, new Handle(value), identifier: 0);
            return !objectId.IsNull;
        }
        catch
        {
            return false;
        }
    }

    private static string DescribeException(System.Exception exception) =>
        exception is AcException autocad
            ? autocad.ErrorStatus.ToString()
            : exception.GetType().Name;

    private static T? Safe<T>(Func<T?> getter)
    {
        try { return getter(); }
        catch { return default; }
    }
}
