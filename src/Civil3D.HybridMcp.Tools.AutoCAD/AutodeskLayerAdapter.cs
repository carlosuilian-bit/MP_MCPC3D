using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Tools.AutoCAD;

public sealed class AutodeskLayerAdapter : ILayerAdapter
{
    private readonly Document _document;
    private readonly DocumentDescriptor _descriptor;

    public AutodeskLayerAdapter(Document document, DocumentDescriptor descriptor)
    {
        _document = document;
        _descriptor = descriptor;
    }

    public IReadOnlyList<LayerInfo> ListLayers()
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var table = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        var layers = new List<LayerInfo>();

        foreach (ObjectId id in table)
        {
            var record = (LayerTableRecord)transaction.GetObject(id, OpenMode.ForRead);
            layers.Add(ToLayerInfo(record, database, _descriptor, transaction));
        }

        transaction.Commit();
        return layers;
    }

    public LayerInfo? GetLayer(LayerLocator locator)
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var table = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);

        LayerTableRecord? record = null;
        if (!string.IsNullOrWhiteSpace(locator.Name) && table.Has(locator.Name))
        {
            record = (LayerTableRecord)transaction.GetObject(table[locator.Name], OpenMode.ForRead);
        }
        else if (!string.IsNullOrWhiteSpace(locator.Handle) &&
            TryGetObjectId(database, locator.Handle, out var objectId) &&
            !objectId.IsNull &&
            !objectId.IsErased)
        {
            var dbObject = transaction.GetObject(objectId, OpenMode.ForRead, openErased: false);
            record = dbObject as LayerTableRecord;
        }

        var result = record is null
            ? null
            : ToLayerInfo(record, database, _descriptor, transaction);
        transaction.Commit();
        return result;
    }

    internal static bool TryGetObjectId(Database database, string handleText, out ObjectId objectId)
    {
        objectId = ObjectId.Null;
        try
        {
            var value = Convert.ToInt64(handleText, 16);
            var handle = new Handle(value);
            objectId = database.GetObjectId(createIfNotFound: false, handle, identifier: 0);
            return !objectId.IsNull;
        }
        catch
        {
            return false;
        }
    }

    internal static LayerInfo ToLayerInfo(
        LayerTableRecord record,
        Database database,
        DocumentDescriptor descriptor,
        Transaction transaction)
    {
        var handle = Safe(() => record.Handle.ToString());
        return new LayerInfo
        {
            Name = record.Name,
            Handle = handle,
            ObjectReference = new CadObjectReference
            {
                InstanceId = descriptor.InstanceId,
                DocumentId = descriptor.DocumentId,
                DatabaseFingerprint = descriptor.DatabaseFingerprint,
                Handle = handle,
                ObjectId = Safe(() => record.ObjectId.ToString()),
                Type = nameof(LayerTableRecord),
                Name = record.Name
            },
            IsCurrent = record.ObjectId == database.Clayer,
            IsOn = !record.IsOff,
            IsFrozen = record.IsFrozen,
            IsLocked = record.IsLocked,
            IsPlottable = record.IsPlottable,
            Color = new LayerColorInfo
            {
                ColorIndex = Safe(() => record.Color.ColorIndex),
                Method = Safe(() => record.Color.ColorMethod.ToString())
            },
            Linetype = Safe(() => GetLinetypeName(record, transaction)),
            Lineweight = Safe(() => record.LineWeight.ToString()),
            Description = Safe(() => string.IsNullOrWhiteSpace(record.Description) ? null : record.Description)
        };
    }

    private static string? GetLinetypeName(LayerTableRecord record, Transaction transaction)
    {
        if (record.LinetypeObjectId.IsNull || record.LinetypeObjectId.IsErased)
        {
            return null;
        }

        return transaction.GetObject(record.LinetypeObjectId, OpenMode.ForRead) is LinetypeTableRecord linetype
            ? linetype.Name
            : null;
    }

    private static T? Safe<T>(Func<T?> getter)
    {
        try { return getter(); }
        catch { return default; }
    }
}

public sealed class AutodeskLayerWriteAdapter : ILayerWriteAdapter
{
    private readonly Document _document;
    private readonly DocumentDescriptor _descriptor;
    private readonly Transaction _transaction;

    public AutodeskLayerWriteAdapter(Document document, DocumentDescriptor descriptor, Transaction transaction)
    {
        _document = document;
        _descriptor = descriptor;
        _transaction = transaction;
    }

    public LayerInfo CreateLayer(LayerCreateRequest request)
    {
        var database = _document.Database;
        var table = (LayerTable)_transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        if (table.Has(request.Name))
        {
            throw new LayerToolException(ErrorCode.LayerAlreadyExists, $"Layer '{request.Name}' already exists in the target drawing.");
        }

        table.UpgradeOpen();
        var record = new LayerTableRecord
        {
            Name = request.Name
        };

        if (request.ColorIndex is { } colorIndex)
        {
            record.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
        }

        if (request.IsPlottable is { } isPlottable)
        {
            record.IsPlottable = isPlottable;
        }

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            record.Description = request.Description;
        }

        table.Add(record);
        _transaction.AddNewlyCreatedDBObject(record, true);
        return AutodeskLayerAdapter.ToLayerInfo(record, database, _descriptor, _transaction);
    }

    public LayerSetCurrentResult SetCurrentLayer(LayerSetCurrentRequest request)
    {
        var locator = LayerLocator.From(request);
        var database = _document.Database;
        var table = (LayerTable)_transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        var previousRecord = (LayerTableRecord)_transaction.GetObject(database.Clayer, OpenMode.ForRead);
        var targetRecord = FindLayerRecord(database, table, locator);
        if (targetRecord is null)
        {
            throw new LayerToolException(ErrorCode.LayerNotFound, "Layer was not found in the target drawing.");
        }

        if (targetRecord.IsFrozen)
        {
            throw new LayerToolException(ErrorCode.InvalidRequest, "Frozen layers cannot be made current.");
        }

        var previous = AutodeskLayerAdapter.ToLayerInfo(previousRecord, database, _descriptor, _transaction);
        var changed = targetRecord.ObjectId != database.Clayer;
        if (changed)
        {
            database.Clayer = targetRecord.ObjectId;
        }

        var current = AutodeskLayerAdapter.ToLayerInfo(targetRecord, database, _descriptor, _transaction);
        return new LayerSetCurrentResult(previous, current, changed);
    }

    public LayerUpdateResult UpdateLayer(LayerUpdateRequest request)
    {
        var locator = LayerLocator.From(request);
        var database = _document.Database;
        var table = (LayerTable)_transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
        var targetRecord = FindLayerRecord(database, table, locator, OpenMode.ForWrite);
        if (targetRecord is null)
        {
            throw new LayerToolException(ErrorCode.LayerNotFound, "Layer was not found in the target drawing.");
        }

        if (targetRecord.ObjectId == database.Clayer && request.IsFrozen == true)
        {
            throw new LayerToolException(ErrorCode.InvalidRequest, "The current layer cannot be frozen.");
        }

        if (targetRecord.ObjectId == database.Clayer && request.IsOn == false)
        {
            throw new LayerToolException(ErrorCode.InvalidRequest, "The current layer cannot be turned off.");
        }

        if (!string.IsNullOrWhiteSpace(request.NewName) &&
            !string.Equals(targetRecord.Name, request.NewName, StringComparison.OrdinalIgnoreCase) &&
            table.Has(request.NewName))
        {
            throw new LayerToolException(ErrorCode.LayerAlreadyExists, $"Layer '{request.NewName}' already exists in the target drawing.");
        }

        var previous = AutodeskLayerAdapter.ToLayerInfo(targetRecord, database, _descriptor, _transaction);
        var changed = false;

        if (!string.IsNullOrWhiteSpace(request.NewName) &&
            !string.Equals(targetRecord.Name, request.NewName, StringComparison.Ordinal))
        {
            targetRecord.Name = request.NewName;
            changed = true;
        }

        if (request.ColorIndex is { } colorIndex &&
            targetRecord.Color.ColorIndex != colorIndex)
        {
            targetRecord.Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
            changed = true;
        }

        if (request.IsOn is { } isOn &&
            targetRecord.IsOff == isOn)
        {
            targetRecord.IsOff = !isOn;
            changed = true;
        }

        if (request.IsFrozen is { } isFrozen &&
            targetRecord.IsFrozen != isFrozen)
        {
            targetRecord.IsFrozen = isFrozen;
            changed = true;
        }

        if (request.IsLocked is { } isLocked &&
            targetRecord.IsLocked != isLocked)
        {
            targetRecord.IsLocked = isLocked;
            changed = true;
        }

        if (request.IsPlottable is { } isPlottable &&
            targetRecord.IsPlottable != isPlottable)
        {
            targetRecord.IsPlottable = isPlottable;
            changed = true;
        }

        if (request.Description is not null &&
            !string.Equals(targetRecord.Description ?? string.Empty, request.Description, StringComparison.Ordinal))
        {
            targetRecord.Description = request.Description;
            changed = true;
        }

        var updated = AutodeskLayerAdapter.ToLayerInfo(targetRecord, database, _descriptor, _transaction);
        return new LayerUpdateResult(previous, updated, changed);
    }

    private LayerTableRecord? FindLayerRecord(
        Database database,
        LayerTable table,
        LayerLocator locator,
        OpenMode openMode = OpenMode.ForRead)
    {
        if (!string.IsNullOrWhiteSpace(locator.Name) && table.Has(locator.Name))
        {
            return (LayerTableRecord)_transaction.GetObject(table[locator.Name], openMode);
        }

        if (!string.IsNullOrWhiteSpace(locator.Handle) &&
            AutodeskLayerAdapter.TryGetObjectId(database, locator.Handle, out var objectId) &&
            !objectId.IsNull &&
            !objectId.IsErased)
        {
            return _transaction.GetObject(objectId, openMode, openErased: false) as LayerTableRecord;
        }

        return null;
    }
}
