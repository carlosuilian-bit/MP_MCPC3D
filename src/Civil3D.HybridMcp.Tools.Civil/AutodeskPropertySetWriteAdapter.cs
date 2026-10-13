using System.Globalization;
using Autodesk.Aec.PropertyData;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Civil3D.HybridMcp.Protocol;
using AecDataType = Autodesk.Aec.PropertyData.DataType;

namespace Civil3D.HybridMcp.Tools.Civil;

public sealed class AutodeskCivilWriteAdapter : ICivilWriteAdapter
{
    private readonly Document _document;
    private readonly DocumentDescriptor _descriptor;
    private readonly Transaction _transaction;

    public AutodeskCivilWriteAdapter(Document document, DocumentDescriptor descriptor, Transaction transaction)
    {
        _document = document;
        _descriptor = descriptor;
        _transaction = transaction;
    }

    public PropertySetNumericTestResult CreatePropertySetNumericTest(PropertySetNumericTestCreateRequest request)
    {
        var database = _document.Database;
        var definitionId = CreateDefinition(database, request.DefinitionName, request.PropertyName);
        var targetId = CreateDbPoint(database);
        var target = (DBObject)_transaction.GetObject(targetId, OpenMode.ForWrite, openErased: false);

        try
        {
            PropertyDataServices.AddPropertySet(target, definitionId);
        }
        catch (Exception ex)
        {
            throw new CivilToolException(ErrorCode.CivilQueryFailed, $"Could not attach test property set: {ex.Message}");
        }

        var propertySetId = GetPropertySetId(target, definitionId);
        var propertySet = (PropertySet)_transaction.GetObject(propertySetId, OpenMode.ForWrite, openErased: false);
        var definition = (PropertySetDefinition)_transaction.GetObject(definitionId, OpenMode.ForRead, openErased: false);
        var propertyId = ResolvePropertyId(propertySet, request.PropertyName);

        try
        {
            propertySet.Synchronize(definition, target);
            propertySet.SetAt(propertyId, request.NumericValue);
        }
        catch (Exception ex)
        {
            throw new CivilToolException(ErrorCode.CivilQueryFailed, $"Could not write test property set value: {ex.Message}");
        }

        var roundTrip = ReadNumericValue(propertySet, propertyId);
        var propertyValue = new PropertyValueInfo
        {
            Name = request.PropertyName,
            Id = propertyId,
            DataType = AecDataType.Real.ToString(),
            Value = FormatValue(roundTrip),
            NumericValue = ToNumeric(roundTrip)
        };

        var propertySetInfo = new PropertySetInfo
        {
            Name = Safe(() => propertySet.PropertySetDefinitionName) ?? request.DefinitionName,
            Handle = Safe(() => propertySet.Handle.ToString()),
            DefinitionHandle = Safe(() => definition.Handle.ToString()),
            PropertyCount = 1,
            Properties = [propertyValue]
        };

        return new PropertySetNumericTestResult(
            request.DefinitionName,
            propertySetInfo.DefinitionHandle,
            targetId.Handle.ToString(),
            request.TargetKind ?? "db_point",
            propertySetInfo);
    }

    private ObjectId CreateDefinition(Database database, string definitionName, string propertyName)
    {
        var dictionary = new DictionaryPropertySetDefinitions(database);
        try
        {
            if (dictionary.Has(definitionName, _transaction))
            {
                throw new CivilToolException(ErrorCode.InvalidRequest, $"Test property set definition '{definitionName}' already exists.");
            }
        }
        catch (CivilToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CivilToolException(ErrorCode.CivilQueryFailed, $"Could not inspect property set definition dictionary: {ex.Message}");
        }

        var definition = new PropertySetDefinition
        {
            AppliesToAll = true,
            IsVisible = true,
            IsWriteable = true
        };

        var property = new PropertyDefinition
        {
            Name = propertyName,
            DataType = AecDataType.Real,
            DefaultData = 0.0,
            IsReadOnly = false,
            IsVisible = true
        };
        definition.Definitions.Add(property);

        try
        {
            dictionary.AddNewRecord(definitionName, definition);
            _transaction.AddNewlyCreatedDBObject(definition, add: true);
            return definition.ObjectId;
        }
        catch (Exception ex)
        {
            throw new CivilToolException(ErrorCode.CivilQueryFailed, $"Could not create test property set definition: {ex.Message}");
        }
    }

    private ObjectId CreateDbPoint(Database database)
    {
        var blockTable = (BlockTable)_transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)_transaction.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        var point = new DBPoint(new Point3d(0, 0, 0));
        var pointId = modelSpace.AppendEntity(point);
        _transaction.AddNewlyCreatedDBObject(point, add: true);
        return pointId;
    }

    private ObjectId GetPropertySetId(DBObject target, ObjectId definitionId)
    {
        try
        {
            var propertySetId = PropertyDataServices.GetPropertySet(target, definitionId);
            if (!propertySetId.IsNull)
            {
                return propertySetId;
            }
        }
        catch
        {
            // Fall back to enumerating below.
        }

        try
        {
            foreach (ObjectId id in PropertyDataServices.GetPropertySets(target))
            {
                if (_transaction.GetObject(id, OpenMode.ForRead, openErased: false) is PropertySet propertySet &&
                    propertySet.PropertySetDefinition == definitionId)
                {
                    return id;
                }
            }
        }
        catch (Exception ex)
        {
            throw new CivilToolException(ErrorCode.CivilQueryFailed, $"Could not resolve attached test property set: {ex.Message}");
        }

        throw new CivilToolException(ErrorCode.PropertySetNotFound, "The test property set was not attached after AddPropertySet.");
    }

    private static int ResolvePropertyId(PropertySet propertySet, string propertyName)
    {
        try
        {
            return propertySet.PropertyNameToId(propertyName);
        }
        catch (Exception ex)
        {
            throw new CivilToolException(ErrorCode.CivilQueryFailed, $"Could not resolve test property id: {ex.Message}");
        }
    }

    private static object? ReadNumericValue(PropertySet propertySet, int propertyId)
    {
        try
        {
            return propertySet.GetAt(propertyId);
        }
        catch (Exception ex)
        {
            throw new CivilToolException(ErrorCode.CivilQueryFailed, $"Could not read test property value after write: {ex.Message}");
        }
    }

    private static string? FormatValue(object? value) =>
        value switch
        {
            null => null,
            double number => number.ToString("0.############", CultureInfo.InvariantCulture),
            float number => number.ToString("0.######", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    private static double? ToNumeric(object? value) =>
        value switch
        {
            double number => number,
            float number => number,
            int number => number,
            long number => number,
            short number => number,
            decimal number => (double)number,
            _ => null
        };

    private static T? Safe<T>(Func<T?> getter)
    {
        try { return getter(); }
        catch { return default; }
    }
}
