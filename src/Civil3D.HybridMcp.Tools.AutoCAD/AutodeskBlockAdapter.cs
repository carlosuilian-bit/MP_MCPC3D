using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Tools.AutoCAD;

/// <summary>
/// Block reads against the live drawing. Everything is opened
/// <see cref="OpenMode.ForRead"/> inside a transaction that only commits, so
/// nothing here can change the drawing.
///
/// Three things this adapter does that a naive reader would not:
///
/// - It resolves a dynamic insert to its <b>effective</b> definition through
///   <c>DynamicBlockTableRecord</c>. The insert itself points at an anonymous
///   representation such as *U12.
/// - It reads <b>constant</b> attributes from the definition, because an insert
///   never carries them.
/// - It filters entities by runtime class through <c>ObjectId.ObjectClass</c>
///   before opening anything, so a sweep of a large drawing opens only the
///   block inserts, not every line and hatch. The exported drawing in this
///   project carries more than eleven thousand hatches.
/// </summary>
public sealed class AutodeskBlockAdapter : IBlockReadAdapter
{
    private readonly Document _document;

    public AutodeskBlockAdapter(Document document)
    {
        _document = document;
    }

    public IReadOnlyList<BlockDefinitionInfo> ListDefinitions()
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var attributeDefinitionClass = RXObject.GetClass(typeof(AttributeDefinition));

        var definitions = new List<BlockDefinitionInfo>();
        foreach (ObjectId recordId in table)
        {
            if (OpenForRead(transaction, recordId) is not BlockTableRecord record)
            {
                continue;
            }

            var isLayout = Safe(() => record.IsLayout);

            // Layouts can hold attribute definitions in principle, but they are
            // not blocks anyone inserts; skipping them also avoids walking every
            // entity in model space.
            var attributeDefinitions = !isLayout && Safe(() => record.HasAttributeDefinitions)
                ? ReadAttributeDefinitions(transaction, record, attributeDefinitionClass)
                : Array.Empty<BlockAttributeDefinitionInfo>();

            definitions.Add(new BlockDefinitionInfo
            {
                Name = Safe(() => record.Name) ?? string.Empty,
                Handle = Safe(() => record.Handle.ToString()),
                IsAnonymous = Safe(() => record.IsAnonymous),
                IsDynamic = Safe(() => record.IsDynamicBlock),
                IsXref = Safe(() => record.IsFromExternalReference),
                IsOverlay = Safe(() => record.IsFromOverlayReference),
                IsLayout = isLayout,
                IsDependent = Safe(() => record.IsDependent),
                HasAttributeDefinitions = attributeDefinitions.Count > 0,
                AttributeDefinitions = attributeDefinitions,
                ReferenceCount = isLayout ? 0 : CountReferences(transaction, record),
                Comments = NullIfEmpty(Safe(() => record.Comments)),
                Units = Safe(() => record.Units.ToString())
            });
        }

        transaction.Commit();
        return definitions;
    }

    public BlockReferenceSweep SweepReferences(int scanBudget, bool includeAttributes)
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();
        var table = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
        var blockReferenceClass = RXObject.GetClass(typeof(BlockReference));
        var context = new ReadContext(transaction);

        var references = new List<BlockReferenceInfo>();
        var scanned = 0;
        var truncated = false;

        foreach (ObjectId ownerId in table)
        {
            if (truncated)
            {
                break;
            }

            if (OpenForRead(transaction, ownerId) is not BlockTableRecord owner || !Safe(() => owner.IsLayout))
            {
                continue;
            }

            var space = LayoutName(transaction, owner);
            foreach (ObjectId entityId in owner)
            {
                if (scanned >= scanBudget)
                {
                    // Stopping with at least one entity still unread: the list
                    // is a prefix and must say so.
                    truncated = true;
                    break;
                }

                scanned++;

                if (entityId.IsErased || !IsDerivedFrom(entityId, blockReferenceClass))
                {
                    continue;
                }

                // A table is implemented as a block reference to an anonymous
                // *T block. Nobody inserted it as a block, so it is not listed.
                if (OpenForRead(transaction, entityId) is not BlockReference reference || reference is Table)
                {
                    continue;
                }

                references.Add(Describe(context, reference, space, includeAttributes));
            }
        }

        transaction.Commit();
        return new BlockReferenceSweep(references, scanned, truncated);
    }

    public BlockAttributeReadResult ReadAttributes(string handle)
    {
        var database = _document.Database;
        using var transaction = database.TransactionManager.StartTransaction();

        if (!TryGetObjectId(database, handle, out var objectId) || objectId.IsErased ||
            OpenForRead(transaction, objectId) is not { } target)
        {
            transaction.Commit();
            return new BlockAttributeReadResult(BlockAttributeReadStatus.NotFound, null, null, [], []);
        }

        if (target is not BlockReference reference || target is Table)
        {
            var objectClass = Safe(() => target.GetRXClass()?.Name) ?? target.GetType().Name;
            transaction.Commit();
            return new BlockAttributeReadResult(BlockAttributeReadStatus.NotABlockReference, objectClass, null, [], []);
        }

        var context = new ReadContext(transaction);
        var info = Describe(context, reference, OwnerName(transaction, reference), includeAttributes: false);
        var instanceAttributes = ReadInstanceAttributes(transaction, reference);
        var constantAttributes = context.ConstantAttributesFor(reference);

        transaction.Commit();
        return new BlockAttributeReadResult(
            BlockAttributeReadStatus.Found,
            Safe(() => reference.GetRXClass()?.Name),
            info,
            instanceAttributes,
            constantAttributes);
    }

    private static BlockReferenceInfo Describe(ReadContext context, BlockReference reference, string? space, bool includeAttributes)
    {
        var transaction = context.Transaction;
        var definition = OpenForRead(transaction, Safe(() => reference.BlockTableRecord)) as BlockTableRecord;
        var isDynamic = Safe(() => reference.IsDynamicBlock);

        // For a dynamic insert, BlockTableRecord is the anonymous representation
        // (*U12) and DynamicBlockTableRecord is the block a person knows by name.
        var effective = isDynamic
            ? OpenForRead(transaction, Safe(() => reference.DynamicBlockTableRecord)) as BlockTableRecord ?? definition
            : definition;

        var definitionName = Safe(() => definition?.Name) ?? string.Empty;
        var position = Safe(() => reference.Position);
        var scale = Safe(() => reference.ScaleFactors);

        return new BlockReferenceInfo
        {
            Handle = Safe(() => reference.Handle.ToString()) ?? string.Empty,
            BlockName = Safe(() => effective?.Name) ?? definitionName,
            DefinitionName = definitionName,
            IsDynamic = isDynamic,
            IsAnonymous = Safe(() => effective?.IsAnonymous ?? false),
            Layer = Safe(() => reference.Layer),
            Space = space,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            RotationRadians = Safe(() => reference.Rotation),
            ScaleX = scale.X,
            ScaleY = scale.Y,
            ScaleZ = scale.Z,
            AttributeCount = Safe(() => reference.AttributeCollection.Count),
            Attributes = includeAttributes
                ? BlockService.MergeConstantAttributes(
                    ReadInstanceAttributes(transaction, reference),
                    context.ConstantAttributesFor(reference))
                : null
        };
    }

    private static IReadOnlyList<BlockAttributeInfo> ReadInstanceAttributes(Transaction transaction, BlockReference reference)
    {
        var attributes = new List<BlockAttributeInfo>();
        var collection = Safe(() => reference.AttributeCollection);
        if (collection is null)
        {
            return attributes;
        }

        foreach (ObjectId attributeId in collection)
        {
            if (attributeId.IsErased || OpenForRead(transaction, attributeId) is not AttributeReference attribute)
            {
                continue;
            }

            attributes.Add(new BlockAttributeInfo
            {
                Tag = Safe(() => attribute.Tag) ?? string.Empty,
                Value = Safe(() => attribute.TextString),
                Handle = Safe(() => attribute.Handle.ToString()),
                Constant = false,
                Invisible = Safe(() => attribute.Invisible),
                Multiline = Safe(() => attribute.IsMTextAttribute)
            });
        }

        return attributes;
    }

    private static IReadOnlyList<BlockAttributeDefinitionInfo> ReadAttributeDefinitions(
        Transaction transaction,
        BlockTableRecord record,
        RXClass attributeDefinitionClass)
    {
        var definitions = new List<BlockAttributeDefinitionInfo>();
        foreach (ObjectId entityId in record)
        {
            if (entityId.IsErased || !IsDerivedFrom(entityId, attributeDefinitionClass) ||
                OpenForRead(transaction, entityId) is not AttributeDefinition definition)
            {
                continue;
            }

            definitions.Add(new BlockAttributeDefinitionInfo
            {
                Tag = Safe(() => definition.Tag) ?? string.Empty,
                Prompt = NullIfEmpty(Safe(() => definition.Prompt)),
                DefaultValue = Safe(() => definition.TextString),
                Constant = Safe(() => definition.Constant),
                Invisible = Safe(() => definition.Invisible),
                Multiline = Safe(() => definition.IsMTextAttributeDefinition)
            });
        }

        return definitions;
    }

    /// <summary>
    /// Inserts of a definition across the drawing. A dynamic block is inserted
    /// mostly through anonymous representations, so their inserts are counted
    /// too; counting the dynamic definition alone would report near zero.
    /// </summary>
    private static int CountReferences(Transaction transaction, BlockTableRecord record)
    {
        var count = CountLive(Safe(() => record.GetBlockReferenceIds(true, false)));

        if (Safe(() => record.IsDynamicBlock))
        {
            var anonymousIds = Safe(() => record.GetAnonymousBlockIds());
            if (anonymousIds is not null)
            {
                foreach (ObjectId anonymousId in anonymousIds)
                {
                    if (OpenForRead(transaction, anonymousId) is BlockTableRecord anonymous)
                    {
                        count += CountLive(Safe(() => anonymous.GetBlockReferenceIds(true, false)));
                    }
                }
            }
        }

        return count;
    }

    private static int CountLive(ObjectIdCollection? ids)
    {
        if (ids is null)
        {
            return 0;
        }

        var count = 0;
        foreach (ObjectId id in ids)
        {
            if (!id.IsNull && !id.IsErased)
            {
                count++;
            }
        }

        return count;
    }

    private static string? LayoutName(Transaction transaction, BlockTableRecord owner) =>
        (OpenForRead(transaction, Safe(() => owner.LayoutId)) as Layout) is { } layout
            ? Safe(() => layout.LayoutName)
            : Safe(() => owner.Name);

    /// <summary>Layout name when the insert sits in a layout; the owning block's name when it is nested in another definition.</summary>
    private static string? OwnerName(Transaction transaction, BlockReference reference) =>
        OpenForRead(transaction, Safe(() => reference.OwnerId)) is BlockTableRecord owner
            ? Safe(() => owner.IsLayout) ? LayoutName(transaction, owner) : Safe(() => owner.Name)
            : null;

    private static bool IsDerivedFrom(ObjectId id, RXClass baseClass)
    {
        try
        {
            return id.ObjectClass?.IsDerivedFrom(baseClass) == true;
        }
        catch
        {
            return false;
        }
    }

    private static DBObject? OpenForRead(Transaction transaction, ObjectId id)
    {
        if (id.IsNull)
        {
            return null;
        }

        try
        {
            return transaction.GetObject(id, OpenMode.ForRead, openErased: false);
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

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static T? Safe<T>(Func<T?> getter) where T : class
    {
        try { return getter(); }
        catch { return null; }
    }

    private static T Safe<T>(Func<T> getter, T fallback = default) where T : struct
    {
        try { return getter(); }
        catch { return fallback; }
    }

    /// <summary>
    /// Per-call cache of constant attributes by definition. A sweep with
    /// attributes may meet thousands of inserts of the same few blocks, and the
    /// definition only needs to be walked once.
    /// </summary>
    private sealed class ReadContext
    {
        private readonly Dictionary<ObjectId, IReadOnlyList<BlockAttributeInfo>> _constants = new();
        private RXClass? _attributeDefinitionClass;

        public ReadContext(Transaction transaction)
        {
            Transaction = transaction;
        }

        public Transaction Transaction { get; }

        public IReadOnlyList<BlockAttributeInfo> ConstantAttributesFor(BlockReference reference)
        {
            // The anonymous representation of a dynamic block normally carries
            // copies of the attribute definitions; fall back to the dynamic
            // definition when it does not.
            var constants = ConstantsOf(Safe(() => reference.BlockTableRecord));
            if (constants.Count == 0 && Safe(() => reference.IsDynamicBlock))
            {
                constants = ConstantsOf(Safe(() => reference.DynamicBlockTableRecord));
            }

            return constants;
        }

        private IReadOnlyList<BlockAttributeInfo> ConstantsOf(ObjectId definitionId)
        {
            if (definitionId.IsNull)
            {
                return [];
            }

            if (_constants.TryGetValue(definitionId, out var cached))
            {
                return cached;
            }

            _attributeDefinitionClass ??= RXObject.GetClass(typeof(AttributeDefinition));
            IReadOnlyList<BlockAttributeInfo> constants =
                OpenForRead(Transaction, definitionId) is BlockTableRecord definition && Safe(() => definition.HasAttributeDefinitions)
                    ? ReadAttributeDefinitions(Transaction, definition, _attributeDefinitionClass)
                        .Where(attribute => attribute.Constant)
                        .Select(attribute => new BlockAttributeInfo
                        {
                            Tag = attribute.Tag,
                            Value = attribute.DefaultValue,
                            Handle = null,
                            Constant = true,
                            Invisible = attribute.Invisible,
                            Multiline = attribute.Multiline
                        })
                        .ToArray()
                    : [];

            _constants[definitionId] = constants;
            return constants;
        }
    }
}
