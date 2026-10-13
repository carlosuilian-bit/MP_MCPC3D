using System.Globalization;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Tools.Civil;

/// <summary>
/// AEC property set reads for <see cref="AutodeskCivilReadAdapter"/>.
///
/// Property sets are an AEC feature, not a Civil 3D one, so they live in
/// <c>AecPropDataMgd</c> rather than the Civil assemblies. Definitions come from
/// the drawing's property set definition dictionary; attached sets come from
/// <see cref="PropertyDataServices"/>.
///
/// Every read is defensive: a drawing can carry definitions whose properties do
/// not resolve, and a single bad property must not fail the whole request.
/// </summary>
public sealed partial class AutodeskCivilReadAdapter : IPropertySetSweepAdapter
{
    /// <summary>
    /// Default scan budget when the caller does not supply one.
    ///
    /// Raised from 50,000 after live validation hit that ceiling on a real
    /// drawing and returned a prefix. Any fixed number is reachable on a large
    /// enough drawing, which is why the caller can override it: this is a
    /// default, not a limit.
    /// </summary>
    private const int DefaultSweepBudget = 250_000;

    /// <summary>
    /// Walks every entity in every block table record looking for one named
    /// property set.
    ///
    /// This is the only property-set operation that does not start from a
    /// handle, which is exactly why it exists: every other one could only be
    /// used by a caller who already had one.
    ///
    /// Both spaces are covered, because roadway annotation lives in paper space
    /// as often as in model space. An object that cannot be read is skipped
    /// rather than failing the sweep: one bad entity in four thousand must not
    /// cost the caller the other three thousand nine hundred.
    /// </summary>
    public PropertySetSweep SweepPropertySets(string setName, int? maxScan)
    {
        var budget = maxScan is { } requested && requested > 0 ? requested : DefaultSweepBudget;
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var carriers = new List<PropertySetCarrier>();
        var scanned = 0;
        var truncated = false;

        try
        {
            var blockTable = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead, openErased: false);

            foreach (ObjectId blockId in blockTable)
            {
                if (truncated)
                {
                    break;
                }

                BlockTableRecord? record;
                try
                {
                    record = transaction.GetObject(blockId, OpenMode.ForRead, openErased: false) as BlockTableRecord;
                }
                catch
                {
                    continue;
                }

                if (record is null)
                {
                    continue;
                }

                foreach (ObjectId entityId in record)
                {
                    if (scanned >= budget)
                    {
                        truncated = true;
                        break;
                    }

                    scanned++;
                    try
                    {
                        if (transaction.GetObject(entityId, OpenMode.ForRead, openErased: false) is not DBObject entity)
                        {
                            continue;
                        }

                        var match = FindAttachedSet(entity, setName, transaction);
                        if (match is not null)
                        {
                            carriers.Add(new PropertySetCarrier(
                                Safe(() => entity.Handle.ToString()) ?? string.Empty,
                                Safe(() => entity.GetRXClass()?.Name),
                                match));
                        }
                    }
                    catch
                    {
                        // One unreadable entity is skipped, never fatal.
                    }
                }
            }
        }
        catch (Exception ex)
        {
            transaction.Commit();
            throw new CivilToolException(ErrorCode.CivilQueryFailed, $"Could not sweep the drawing for property sets: {ex.Message}");
        }

        transaction.Commit();
        return new PropertySetSweep(carriers, scanned, truncated);
    }

    private PropertySetInfo? FindAttachedSet(DBObject target, string setName, Transaction transaction)
    {
        ObjectIdCollection attached;
        try
        {
            attached = PropertyDataServices.GetPropertySets(target);
        }
        catch
        {
            // An object with no property data throws rather than returning empty.
            return null;
        }

        foreach (ObjectId setId in attached)
        {
            try
            {
                if (transaction.GetObject(setId, OpenMode.ForRead, openErased: false) is PropertySet propertySet)
                {
                    var info = ToPropertySetInfo(propertySet, transaction);
                    if (string.Equals(info.Name, setName, StringComparison.OrdinalIgnoreCase))
                    {
                        return info;
                    }
                }
            }
            catch
            {
                // Skip an unreadable set rather than failing the object.
            }
        }

        return null;
    }

    public IReadOnlyList<PropertySetDefinitionInfo> ListPropertySetDefinitions()
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var definitions = new List<PropertySetDefinitionInfo>();

        ObjectIdCollection records;
        try
        {
            records = new DictionaryPropertySetDefinitions(database).Records;
        }
        catch
        {
            transaction.Commit();
            return definitions;
        }

        foreach (ObjectId id in records)
        {
            try
            {
                if (transaction.GetObject(id, OpenMode.ForRead, openErased: false) is PropertySetDefinition definition)
                {
                    definitions.Add(ToDefinitionInfo(definition));
                }
            }
            catch
            {
                // A definition that cannot be opened is skipped, not fatal.
            }
        }

        transaction.Commit();
        return definitions;
    }

    public IReadOnlyList<PropertySetInfo>? ListPropertySets(string objectHandle)
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();

        if (!TryGetObjectId(database, objectHandle, out var objectId) || objectId.IsNull || objectId.IsErased)
        {
            transaction.Commit();
            return null;
        }

        DBObject target;
        try
        {
            target = transaction.GetObject(objectId, OpenMode.ForRead, openErased: false);
        }
        catch
        {
            transaction.Commit();
            return null;
        }

        var sets = new List<PropertySetInfo>();
        ObjectIdCollection attached;
        try
        {
            attached = PropertyDataServices.GetPropertySets(target);
        }
        catch
        {
            // An object with no property data throws rather than returning empty.
            transaction.Commit();
            return sets;
        }

        foreach (ObjectId setId in attached)
        {
            try
            {
                if (transaction.GetObject(setId, OpenMode.ForRead, openErased: false) is PropertySet propertySet)
                {
                    sets.Add(ToPropertySetInfo(propertySet, transaction));
                }
            }
            catch
            {
                // Skip an unreadable set rather than failing the object.
            }
        }

        transaction.Commit();
        return sets;
    }

    private PropertySetDefinitionInfo ToDefinitionInfo(PropertySetDefinition definition)
    {
        var properties = new List<PropertyDefinitionInfo>();
        try
        {
            foreach (PropertyDefinition property in definition.Definitions)
            {
                properties.Add(new PropertyDefinitionInfo
                {
                    Name = Safe(() => property.Name) ?? string.Empty,
                    Id = Safe(() => (int?)property.Id),
                    DataType = Safe(() => property.DataType.ToString()),
                    IsReadOnly = Safe(() => (bool?)property.IsReadOnly),
                    IsVisible = Safe(() => (bool?)property.IsVisible),
                    IsAutomatic = Safe(() => (bool?)property.Automatic),
                    Units = Safe(() => property.UnitType.ToString()),
                    DefaultValue = Safe(() => FormatValue(property.DefaultData))
                });
            }
        }
        catch
        {
            // Keep whatever was read before the failure.
        }

        return new PropertySetDefinitionInfo
        {
            Name = Safe(() => definition.Name) ?? string.Empty,
            Handle = Safe(() => definition.Handle.ToString()),
            IsStyleBased = Safe(() => (bool?)definition.IsStyleBased),
            IsVisible = Safe(() => (bool?)definition.IsVisible),
            IsWriteable = Safe(() => (bool?)definition.IsWriteable),
            AppliesToAll = Safe(() => (bool?)definition.AppliesToAll),
            AppliesTo = ReadAppliesTo(definition),
            PropertyCount = properties.Count,
            Properties = properties
        };
    }

    private static IReadOnlyList<string> ReadAppliesTo(PropertySetDefinition definition)
    {
        try
        {
            var filter = definition.AppliesToFilter;
            return filter is null
                ? []
                : filter.Cast<string>().Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
        }
        catch
        {
            return [];
        }
    }

    private PropertySetInfo ToPropertySetInfo(PropertySet propertySet, Transaction transaction)
    {
        var values = new List<PropertyValueInfo>();
        PropertySetDefinition? definition = null;
        try
        {
            definition = transaction.GetObject(propertySet.PropertySetDefinition, OpenMode.ForRead, openErased: false) as PropertySetDefinition;
        }
        catch
        {
            // Values are still readable without the definition; names degrade to ids.
        }

        try
        {
            foreach (PropertySetData data in propertySet.PropertySetData)
            {
                var id = Safe(() => (int?)data.Id);
                var raw = Safe(() => data.GetData());
                values.Add(new PropertyValueInfo
                {
                    Name = ResolvePropertyName(definition, propertySet, id) ?? string.Empty,
                    Id = id,
                    DataType = Safe(() => data.DataType.ToString()),
                    Value = FormatValue(raw),
                    NumericValue = ToNumeric(raw),
                    Units = Safe(() => data.UnitType.ToString()),
                    IsReadOnly = ResolveIsReadOnly(definition, id)
                });
            }
        }
        catch
        {
            // Keep whatever was read before the failure.
        }

        return new PropertySetInfo
        {
            Name = Safe(() => propertySet.PropertySetDefinitionName)
                ?? Safe(() => definition?.Name)
                ?? string.Empty,
            Handle = Safe(() => propertySet.Handle.ToString()),
            DefinitionHandle = Safe(() => propertySet.PropertySetDefinition.Handle.ToString()),
            PropertyCount = values.Count,
            Properties = values
        };
    }

    private static string? ResolvePropertyName(PropertySetDefinition? definition, PropertySet propertySet, int? id)
    {
        if (id is not { } propertyId)
        {
            return null;
        }

        var fromSet = Safe(() => propertySet.PropertyIdToName(propertyId));
        if (!string.IsNullOrWhiteSpace(fromSet))
        {
            return fromSet;
        }

        if (definition is null)
        {
            return null;
        }

        try
        {
            foreach (PropertyDefinition property in definition.Definitions)
            {
                if (Safe(() => (int?)property.Id) == propertyId)
                {
                    return Safe(() => property.Name);
                }
            }
        }
        catch
        {
            // fall through
        }

        return null;
    }

    private static bool? ResolveIsReadOnly(PropertySetDefinition? definition, int? id)
    {
        if (definition is null || id is not { } propertyId)
        {
            return null;
        }

        try
        {
            foreach (PropertyDefinition property in definition.Definitions)
            {
                if (Safe(() => (int?)property.Id) == propertyId)
                {
                    return Safe(() => (bool?)property.IsReadOnly);
                }
            }
        }
        catch
        {
            // fall through
        }

        return null;
    }

    private static string? FormatValue(object? value) =>
        value switch
        {
            null => null,
            string text => text,
            double number => number.ToString("0.############", CultureInfo.InvariantCulture),
            float number => number.ToString("0.######", CultureInfo.InvariantCulture),
            bool flag => flag ? "true" : "false",
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
}
