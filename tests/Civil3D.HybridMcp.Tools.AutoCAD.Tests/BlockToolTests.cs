using System.Text.Json;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;
using Civil3D.HybridMcp.Tools.AutoCAD;

namespace Civil3D.HybridMcp.Tools.AutoCAD.Tests;

/// <summary>
/// Block reads: definitions, inserted references, and attribute values.
///
/// Blocks are plain AutoCAD entities, so unlike the Civil 3D reads these keep
/// working after a project is exported. The rules under test are the ones that
/// fail silently when missed: a dynamic block insert carries an anonymous
/// definition name, constant attributes live only on the definition, and a
/// capped scan must say it was capped.
/// </summary>
public sealed class BlockToolTests
{
    [Fact]
    public void BlockListHidesAnonymousAndLayoutDefinitionsByDefault()
    {
        var response = new BlockService().ListBlocks(new FakeBlockAdapter(), SampleDocument(), ListRequest());

        Assert.Equal(new[] { "MARCO_KM", "PLACA" }, response.Blocks.Select(block => block.Name));
        Assert.Equal(2, response.Count);
    }

    [Fact]
    public void BlockListIncludesAnonymousAndLayoutsWhenAsked()
    {
        var response = new BlockService().ListBlocks(
            new FakeBlockAdapter(),
            SampleDocument(),
            ListRequest() with { IncludeAnonymous = true, IncludeLayouts = true });

        Assert.Equal(5, response.Count);
        Assert.Contains(response.Blocks, block => block.Name == "*U12");
        Assert.Contains(response.Blocks, block => block.Name == "*Model_Space");
    }

    [Fact]
    public void BlockListFiltersByNameCaseInsensitively()
    {
        var response = new BlockService().ListBlocks(
            new FakeBlockAdapter(),
            SampleDocument(),
            ListRequest() with { NameContains = "placa" });

        var block = Assert.Single(response.Blocks);
        Assert.Equal("PLACA", block.Name);
    }

    [Fact]
    public void BlockListRejectsAnotherDocument()
    {
        var error = Assert.Throws<BlockToolException>(() => new BlockService().ListBlocks(
            new FakeBlockAdapter(),
            SampleDocument(),
            ListRequest() with { DocumentId = "doc-other" }));

        Assert.Equal(ErrorCode.DocumentNotFound, error.Code);
    }

    [Fact]
    public void ReferenceListFindsDynamicInsertsByTheirRealName()
    {
        // A dynamic block insert points at an anonymous definition such as *U12.
        // Filtering on the definition name alone would return nothing and look
        // like a correct empty answer.
        var response = new BlockService().ListReferences(
            new FakeBlockAdapter(),
            SampleDocument(),
            ReferenceRequest() with { BlockName = "placa" });

        Assert.Equal(new[] { "A1", "A2" }, response.References.Select(reference => reference.Handle));
        Assert.All(response.References, reference => Assert.Equal("PLACA", reference.BlockName));
        Assert.Contains(response.References, reference => reference.DefinitionName == "*U12" && reference.IsDynamic);
    }

    [Fact]
    public void ReferenceListHidesAnonymousInsertsUnlessAsked()
    {
        var service = new BlockService();

        var hidden = service.ListReferences(new FakeBlockAdapter(), SampleDocument(), ReferenceRequest());
        var shown = service.ListReferences(new FakeBlockAdapter(), SampleDocument(), ReferenceRequest() with { IncludeAnonymous = true });

        Assert.DoesNotContain(hidden.References, reference => reference.Handle == "B9");
        Assert.Contains(shown.References, reference => reference.Handle == "B9");
    }

    [Fact]
    public void ReferenceListReturnsAnAnonymousInsertWhenItIsNamedExplicitly()
    {
        var response = new BlockService().ListReferences(
            new FakeBlockAdapter(),
            SampleDocument(),
            ReferenceRequest() with { BlockName = "*U40" });

        var reference = Assert.Single(response.References);
        Assert.Equal("B9", reference.Handle);
    }

    [Fact]
    public void ReferenceListFiltersByLayerCaseInsensitively()
    {
        var response = new BlockService().ListReferences(
            new FakeBlockAdapter(),
            SampleDocument(),
            ReferenceRequest() with { Layer = "f-hz-estaca" });

        var reference = Assert.Single(response.References);
        Assert.Equal("K1", reference.Handle);
    }

    [Fact]
    public void ReferenceListSaysWhenTheResultLimitCutTheList()
    {
        var response = new BlockService().ListReferences(
            new FakeBlockAdapter(),
            SampleDocument(),
            ReferenceRequest() with { MaxResults = 2 });

        Assert.Equal(2, response.Count);
        Assert.True(response.Truncated);
        Assert.Equal("result_limit", response.TruncatedBy);
    }

    [Fact]
    public void ReferenceListSaysWhenTheScanBudgetRanOut()
    {
        var response = new BlockService().ListReferences(
            new FakeBlockAdapter { SweepTruncated = true },
            SampleDocument(),
            ReferenceRequest());

        Assert.True(response.Truncated);
        Assert.Equal("scan_budget", response.TruncatedBy);
        Assert.Equal(FakeBlockAdapter.ScannedCount, response.ScannedCount);
    }

    [Fact]
    public void ReferenceListIsNotTruncatedWhenEverythingFits()
    {
        var response = new BlockService().ListReferences(new FakeBlockAdapter(), SampleDocument(), ReferenceRequest());

        Assert.False(response.Truncated);
        Assert.Null(response.TruncatedBy);
    }

    [Fact]
    public void ReferenceListPassesTheScanBudgetToTheAdapter()
    {
        var adapter = new FakeBlockAdapter();

        new BlockService().ListReferences(adapter, SampleDocument(), ReferenceRequest() with { MaxScan = 1234 });
        Assert.Equal(1234, adapter.LastBudget);

        new BlockService().ListReferences(adapter, SampleDocument(), ReferenceRequest());
        Assert.Equal(BlockService.DefaultScanBudget, adapter.LastBudget);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-5, null)]
    [InlineData(null, 0)]
    [InlineData(null, BlockService.MaxResultsCap + 1)]
    public void ReferenceListRejectsLimitsOutsideTheirRange(int? maxScan, int? maxResults)
    {
        var error = Assert.Throws<BlockToolException>(() => new BlockService().ListReferences(
            new FakeBlockAdapter(),
            SampleDocument(),
            ReferenceRequest() with { MaxScan = maxScan, MaxResults = maxResults }));

        Assert.Equal(ErrorCode.InvalidRequest, error.Code);
    }

    [Fact]
    public void ReferenceListOmitsAttributesUnlessAsked()
    {
        var adapter = new FakeBlockAdapter();
        var service = new BlockService();

        var without = service.ListReferences(adapter, SampleDocument(), ReferenceRequest());
        Assert.False(adapter.LastIncludeAttributes);
        Assert.All(without.References, reference => Assert.Null(reference.Attributes));

        service.ListReferences(adapter, SampleDocument(), ReferenceRequest() with { IncludeAttributes = true });
        Assert.True(adapter.LastIncludeAttributes);
    }

    [Fact]
    public void GetAttributesReturnsConstantAttributesThatTheInsertDoesNotCarry()
    {
        // Constant attributes are stored only on the block definition, never on
        // the insert. Reading the insert alone would drop them silently.
        var response = new BlockService().GetAttributes(new FakeBlockAdapter(), SampleDocument(), AttributesRequest("K1"));

        Assert.Equal(3, response.Count);
        Assert.Equal(new[] { "KM", "RODOVIA", "TIPO" }, response.Attributes.Select(attribute => attribute.Tag));

        var constant = Assert.Single(response.Attributes, attribute => attribute.Constant);
        Assert.Equal("TIPO", constant.Tag);
        Assert.Equal("MARCO", constant.Value);
        Assert.Null(constant.Handle);
        Assert.All(response.Attributes.Where(attribute => !attribute.Constant), attribute => Assert.NotNull(attribute.Handle));
    }

    [Fact]
    public void GetAttributesDoesNotRepeatATagTheInsertAlreadyCarries()
    {
        var response = new BlockService().GetAttributes(
            new FakeBlockAdapter { ConstantDuplicatesInstanceTag = true },
            SampleDocument(),
            AttributesRequest("K1"));

        Assert.Single(response.Attributes, attribute => attribute.Tag == "KM");
    }

    [Fact]
    public void GetAttributesReportsTheBlockItBelongsTo()
    {
        var response = new BlockService().GetAttributes(new FakeBlockAdapter(), SampleDocument(), AttributesRequest("K1"));

        Assert.Equal("K1", response.Handle);
        Assert.Equal("MARCO_KM", response.BlockName);
        Assert.Equal("MARCO_KM", response.DefinitionName);
    }

    [Fact]
    public void GetAttributesFailsStructurallyForAnUnknownHandle()
    {
        var error = Assert.Throws<BlockToolException>(() =>
            new BlockService().GetAttributes(new FakeBlockAdapter(), SampleDocument(), AttributesRequest("DEADBEEF")));

        Assert.Equal(ErrorCode.ObjectNotFound, error.Code);
    }

    [Fact]
    public void GetAttributesNamesTheRealClassWhenTheHandleIsNotABlockInsert()
    {
        var error = Assert.Throws<BlockToolException>(() =>
            new BlockService().GetAttributes(new FakeBlockAdapter(), SampleDocument(), AttributesRequest("L7")));

        Assert.Equal(ErrorCode.InvalidRequest, error.Code);
        Assert.Contains("AcDbPolyline", error.Message);
    }

    [Fact]
    public void GetAttributesRequiresAHandle()
    {
        var error = Assert.Throws<BlockToolException>(() =>
            new BlockService().GetAttributes(new FakeBlockAdapter(), SampleDocument(), AttributesRequest(null)));

        Assert.Equal(ErrorCode.InvalidRequest, error.Code);
    }

    [Fact]
    public void GetAttributesAcceptsTheHandleFromAnObjectReference()
    {
        var request = AttributesRequest(null) with
        {
            ObjectReference = new CadObjectReference
            {
                InstanceId = "instance-1",
                DocumentId = "doc-1",
                Handle = "K1",
                Type = "AcDbBlockReference"
            }
        };

        var response = new BlockService().GetAttributes(new FakeBlockAdapter(), SampleDocument(), request);

        Assert.Equal("K1", response.Handle);
    }

    [Fact]
    public void ResponsesSerializeWithSnakeCaseFieldNames()
    {
        var response = new BlockService().ListReferences(
            new FakeBlockAdapter(),
            SampleDocument(),
            ReferenceRequest() with { MaxResults = 1 });

        var json = JsonSerializer.Serialize(response, JsonContractOptions.Default);

        // Every multi-word field is named explicitly, so these tools do not add
        // to the camelCase/snake_case split tracked as defect D2.
        foreach (var field in new[]
                 {
                     "\"instance_id\"", "\"document_id\"", "\"scanned_count\"", "\"truncated_by\"",
                     "\"block_name\"", "\"definition_name\"", "\"is_dynamic\"", "\"is_anonymous\"",
                     "\"rotation_radians\"", "\"scale_x\"", "\"attribute_count\""
                 })
        {
            Assert.Contains(field, json);
        }

        Assert.DoesNotContain("\"blockName\"", json);
        Assert.DoesNotContain("\"scannedCount\"", json);
    }

    [Fact]
    public void CatalogPublishesTheThreeBlockReadsAsLowRiskReads()
    {
        var tools = AutoCadToolCatalog.ListTools();
        var blockTools = tools.Where(tool => tool.Name.StartsWith("block_", StringComparison.Ordinal)).ToArray();

        Assert.Equal(
            new[] { AutoCadToolNames.BlockList, AutoCadToolNames.BlockReferenceList, AutoCadToolNames.BlockGetAttributes },
            blockTools.Select(tool => tool.Name));

        foreach (var tool in blockTools)
        {
            Assert.Equal(OperationClass.Read, tool.OperationClass);
            Assert.Equal(ToolRisk.Low, tool.Risk);
            Assert.Equal(RollbackSemantics.NoneRequired, tool.RollbackSemantics);
            Assert.False(tool.RequiresConfirmation);
            Assert.Equal("AutoCAD", tool.Module);
        }
    }

    [Fact]
    public void BlockSchemasRequireTheDocumentTargetAndRejectUnknownFields()
    {
        foreach (var schema in new[] { BlockSchemas.BlockListInput(), BlockSchemas.BlockReferenceListInput(), BlockSchemas.BlockGetAttributesInput() })
        {
            var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

            Assert.Contains("instance_id", required);
            Assert.Contains("document_id", required);
            Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        }
    }

    private static BlockListRequest ListRequest() =>
        new()
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1"
        };

    private static BlockReferenceListRequest ReferenceRequest() =>
        new()
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1"
        };

    private static BlockGetAttributesRequest AttributesRequest(string? handle) =>
        new()
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Handle = handle
        };

    private static DocumentDescriptor SampleDocument() =>
        new()
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "Drawing1.dwg",
            FullPath = null,
            IsActive = true,
            IsSaved = false,
            IsReadOnly = false,
            Units = "Meters",
            DatabaseFingerprint = "fingerprint-1",
            SessionIdentifier = "session-1"
        };

    /// <summary>
    /// A small drawing: a plain block with a constant attribute, a dynamic block
    /// whose inserts point at the anonymous definition *U12, a stray anonymous
    /// insert *U40, model space, and a polyline to prove type rejection.
    /// </summary>
    private sealed class FakeBlockAdapter : IBlockReadAdapter
    {
        public const int ScannedCount = 4146;

        public bool SweepTruncated { get; init; }

        public bool ConstantDuplicatesInstanceTag { get; init; }

        public int LastBudget { get; private set; }

        public bool LastIncludeAttributes { get; private set; }

        public IReadOnlyList<BlockDefinitionInfo> ListDefinitions() =>
        [
            Definition("PLACA", isDynamic: true),
            Definition("MARCO_KM", attributeTags: ["KM", "RODOVIA", "TIPO"]),
            Definition("*U12", isAnonymous: true),
            Definition("*U40", isAnonymous: true),
            Definition("*Model_Space", isLayout: true)
        ];

        public BlockReferenceSweep SweepReferences(int scanBudget, bool includeAttributes)
        {
            LastBudget = scanBudget;
            LastIncludeAttributes = includeAttributes;

            return new BlockReferenceSweep(
            [
                Reference("A1", "PLACA", "*U12", "F-HZ-SINALIZACAO", isDynamic: true, includeAttributes),
                Reference("A2", "PLACA", "PLACA", "F-HZ-SINALIZACAO", isDynamic: false, includeAttributes),
                Reference("K1", "MARCO_KM", "MARCO_KM", "F-HZ-ESTACA", isDynamic: false, includeAttributes),
                Reference("B9", "*U40", "*U40", "0", isDynamic: false, includeAttributes, isAnonymous: true)
            ],
            ScannedCount,
            SweepTruncated);
        }

        public BlockAttributeReadResult ReadAttributes(string handle)
        {
            if (string.Equals(handle, "L7", StringComparison.OrdinalIgnoreCase))
            {
                return new BlockAttributeReadResult(BlockAttributeReadStatus.NotABlockReference, "AcDbPolyline", null, [], []);
            }

            if (!string.Equals(handle, "K1", StringComparison.OrdinalIgnoreCase))
            {
                return new BlockAttributeReadResult(BlockAttributeReadStatus.NotFound, null, null, [], []);
            }

            IReadOnlyList<BlockAttributeInfo> constants = ConstantDuplicatesInstanceTag
                ? [Attribute("KM", "999+000", handle: null, constant: true), Attribute("TIPO", "MARCO", handle: null, constant: true)]
                : [Attribute("TIPO", "MARCO", handle: null, constant: true)];

            return new BlockAttributeReadResult(
                BlockAttributeReadStatus.Found,
                "AcDbBlockReference",
                Reference("K1", "MARCO_KM", "MARCO_KM", "F-HZ-ESTACA", isDynamic: false, includeAttributes: false),
                [Attribute("KM", "172+700", handle: "K2"), Attribute("RODOVIA", "BR-393", handle: "K3")],
                constants);
        }

        private static BlockDefinitionInfo Definition(
            string name,
            bool isAnonymous = false,
            bool isDynamic = false,
            bool isLayout = false,
            IReadOnlyList<string>? attributeTags = null) =>
            new()
            {
                Name = name,
                Handle = "H-" + name,
                IsAnonymous = isAnonymous,
                IsDynamic = isDynamic,
                IsLayout = isLayout,
                HasAttributeDefinitions = attributeTags is { Count: > 0 },
                AttributeDefinitions = (attributeTags ?? [])
                    .Select(tag => new BlockAttributeDefinitionInfo { Tag = tag, Constant = tag == "TIPO" })
                    .ToArray()
            };

        private static BlockReferenceInfo Reference(
            string handle,
            string blockName,
            string definitionName,
            string layer,
            bool isDynamic,
            bool includeAttributes,
            bool isAnonymous = false) =>
            new()
            {
                Handle = handle,
                BlockName = blockName,
                DefinitionName = definitionName,
                IsDynamic = isDynamic,
                IsAnonymous = isAnonymous,
                Layer = layer,
                Space = "Model",
                X = 1000.5,
                Y = 2000.25,
                Z = 0,
                RotationRadians = 0,
                ScaleX = 1,
                ScaleY = 1,
                ScaleZ = 1,
                AttributeCount = 0,
                Attributes = includeAttributes ? [] : null
            };

        private static BlockAttributeInfo Attribute(string tag, string value, string? handle, bool constant = false) =>
            new()
            {
                Tag = tag,
                Value = value,
                Handle = handle,
                Constant = constant
            };
    }
}
