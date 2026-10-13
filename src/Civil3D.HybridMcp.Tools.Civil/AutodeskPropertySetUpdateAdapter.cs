using System.Globalization;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Civil3D.HybridMcp.Protocol;
using AecDataType = Autodesk.Aec.PropertyData.DataType;

namespace Civil3D.HybridMcp.Tools.Civil;

/// <summary>
/// Updates values in an AEC property set already attached to a production
/// object.
///
/// Unlike the Phase 3C probe, which created its own definition and its own
/// temporary <c>DBPoint</c>, this one touches an object an engineer made. So it
/// only ever calls <see cref="PropertySet.SetAt"/> on a property that already
/// exists: it never calls <c>AddPropertySet</c>, never adds a definition, and
/// never creates an object.
///
/// Values arrive as invariant text, matching how the read tools report them, and
/// are converted to the property's declared AEC type before writing. Handing
/// <c>SetAt</c> a string for a Real property is how a number silently becomes
/// text in the drawing.
/// </summary>
public sealed class AutodeskPropertySetUpdateAdapter : IPropertySetWriteAdapter
{
    private readonly Document _document;
    private readonly Transaction _transaction;

    public AutodeskPropertySetUpdateAdapter(Document document, Transaction transaction)
    {
        _document = document;
        _transaction = transaction;
    }

    public PropertySetReadState Read(string handle, string setName)
    {
        var target = OpenObject(handle, OpenMode.ForRead);
        if (target is null)
        {
            return new PropertySetReadState(false, null);
        }

        var propertySet = FindPropertySet(target, setName, OpenMode.ForRead);
        return new PropertySetReadState(true, propertySet is null ? null : Describe(propertySet));
    }

    public IReadOnlyList<PropertyValueInfo> Apply(
        string handle,
        string setName,
        IReadOnlyList<PropertySetValueUpdate> updates)
    {
        var target = OpenObject(handle, OpenMode.ForRead)
            ?? throw new CivilToolException(
                ErrorCode.ObjectNotFound,
                $"Handle '{handle}' stopped resolving between preview and write.");

        var propertySet = FindPropertySet(target, setName, OpenMode.ForWrite)
            ?? throw new CivilToolException(
                ErrorCode.PropertySetNotFound,
                $"Property set '{setName}' stopped being attached to '{handle}' between preview and write.");

        foreach (var update in updates)
        {
            var data = FindData(propertySet, update.Name)
                ?? throw new CivilToolException(
                    ErrorCode.PropertyNotFound,
                    $"Property '{update.Name}' is not present on property set '{setName}'.");

            var id = Safe(() => (int?)data.Id)
                ?? throw new CivilToolException(
                    ErrorCode.CivilQueryFailed,
                    $"Could not resolve the property id for '{update.Name}'.");

            var typed = Convert(update.Name, Safe(() => (AecDataType?)data.DataType), update.Value);

            try
            {
                propertySet.SetAt(id, typed);
            }
            catch (Exception ex)
            {
                throw new CivilToolException(
                    ErrorCode.CivilQueryFailed,
                    $"Could not write property '{update.Name}' on set '{setName}': {ex.Message}");
            }
        }

        // Read back from the drawing rather than echoing the request, so the
        // caller is told what the file now holds.
        return Describe(propertySet).Properties;
    }

    /// <summary>
    /// Converts invariant text to the property's declared AEC type. An
    /// unconvertible value fails here rather than being written as something
    /// the caller did not ask for.
    /// </summary>
    private static object Convert(string name, AecDataType? dataType, string value)
    {
        try
        {
            return dataType switch
            {
                AecDataType.Real => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture),
                AecDataType.Integer => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
                AecDataType.TrueFalse => bool.Parse(value),
                _ => value
            };
        }
        catch (Exception ex)
        {
            throw new CivilToolException(
                ErrorCode.InvalidRequest,
                $"Value '{value}' is not valid for property '{name}' of type {dataType}: {ex.Message}");
        }
    }

    private DBObject? OpenObject(string handleText, OpenMode mode)
    {
        var database = _document.Database;
        try
        {
            var value = System.Convert.ToInt64(handleText, 16);
            var objectId = database.GetObjectId(createIfNotFound: false, new Handle(value), identifier: 0);
            if (objectId.IsNull || objectId.IsErased)
            {
                return null;
            }

            return _transaction.GetObject(objectId, mode, openErased: false);
        }
        catch
        {
            return null;
        }
    }

    private PropertySet? FindPropertySet(DBObject target, string setName, OpenMode mode)
    {
        ObjectIdCollection attached;
        try
        {
            attached = PropertyDataServices.GetPropertySets(target);
        }
        catch
        {
            // An object carrying no property data throws rather than returning empty.
            return null;
        }

        foreach (ObjectId id in attached)
        {
            try
            {
                if (_transaction.GetObject(id, mode, openErased: false) is PropertySet candidate &&
                    string.Equals(ReadSetName(candidate), setName, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
            catch
            {
                // An unreadable set is skipped; it is not the one being named.
            }
        }

        return null;
    }

    private string? ReadSetName(PropertySet propertySet)
    {
        var name = Safe(() => propertySet.PropertySetDefinitionName);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return Safe(() =>
            _transaction.GetObject(propertySet.PropertySetDefinition, OpenMode.ForRead, openErased: false)
                is PropertySetDefinition definition
                ? definition.Name
                : null);
    }

    private static PropertySetData? FindData(PropertySet propertySet, string propertyName)
    {
        int id;
        try
        {
            id = propertySet.PropertyNameToId(propertyName);
        }
        catch
        {
            return null;
        }

        foreach (PropertySetData data in propertySet.PropertySetData)
        {
            if (Safe(() => (int?)data.Id) == id)
            {
                return data;
            }
        }

        return null;
    }

    private PropertySetInfo Describe(PropertySet propertySet)
    {
        var values = new List<PropertyValueInfo>();

        foreach (PropertySetData data in propertySet.PropertySetData)
        {
            var id = Safe(() => (int?)data.Id);
            var raw = Safe(() => data.GetData());
            values.Add(new PropertyValueInfo
            {
                Name = Safe(() => id is null ? null : propertySet.PropertyIdToName(id.Value)) ?? string.Empty,
                Id = id,
                DataType = Safe(() => data.DataType.ToString()),
                Value = FormatValue(raw),
                NumericValue = ToNumeric(raw)
            });
        }

        return new PropertySetInfo
        {
            Name = ReadSetName(propertySet) ?? string.Empty,
            Handle = Safe(() => propertySet.Handle.ToString()),
            DefinitionHandle = Safe(() =>
                _transaction.GetObject(propertySet.PropertySetDefinition, OpenMode.ForRead, openErased: false)?.Handle.ToString()),
            PropertyCount = values.Count,
            Properties = values
        };
    }

    private static string? FormatValue(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        _ => System.Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    private static double? ToNumeric(object? value) => value switch
    {
        double number => number,
        float number => number,
        int number => number,
        long number => number,
        short number => number,
        _ => null
    };

    private static T? Safe<T>(Func<T?> getter)
    {
        try { return getter(); }
        catch { return default; }
    }
}
