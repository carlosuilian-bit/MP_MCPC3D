using System.Text.Json;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;
using Civil3D.HybridMcp.Tools.AutoCAD;

namespace Civil3D.HybridMcp.Tools.AutoCAD.Tests;

public sealed class LayerToolTests
{
    [Fact]
    public void LayerResultSerializesWithStructuredIdentity()
    {
        var layer = SampleLayer("0") with
        {
            IsCurrent = true,
            ObjectReference = SampleReference("0", "A1")
        };
        var response = new LayerGetResponse
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Layer = layer
        };

        var json = JsonSerializer.Serialize(response, JsonContractOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<LayerGetResponse>(json, JsonContractOptions.Default);

        Assert.Contains("\"document_id\"", json);
        Assert.Contains("\"object_reference\"", json);
        Assert.Contains("\"is_current\"", json);
        Assert.NotNull(roundTrip);
        Assert.Equal("0", roundTrip!.Layer.Name);
        Assert.Equal("A1", roundTrip.Layer.ObjectReference?.Handle);
    }

    [Fact]
    public void LayerFiltersMatchSupportedPredicates()
    {
        var layer = SampleLayer("C-ROAD-CNTR") with
        {
            IsOn = true,
            IsFrozen = false,
            IsLocked = true,
            IsPlottable = false
        };

        Assert.True(LayerFilters.Matches(layer, new LayerListRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            NameContains = "road",
            IsOn = true,
            IsFrozen = false,
            IsLocked = true,
            IsPlottable = false
        }));
        Assert.False(LayerFilters.Matches(layer, new LayerListRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            NameContains = "survey"
        }));
    }

    [Fact]
    public void LayerIdentityCarriesDocumentDatabaseAndHandle()
    {
        var reference = SampleReference("0", "ABCD");

        Assert.Equal("instance-1", reference.InstanceId);
        Assert.Equal("doc-1", reference.DocumentId);
        Assert.Equal("fingerprint-1", reference.DatabaseFingerprint);
        Assert.Equal("ABCD", reference.Handle);
        Assert.Equal("LayerTableRecord", reference.Type);
        Assert.True(reference.HasLayerLocator);
    }

    [Fact]
    public void InvalidLayerNameIsRejected()
    {
        var ex = Assert.Throws<LayerToolException>(() => LayerRequestValidator.ValidateLayerNameSyntax("bad:name"));

        Assert.Equal(ErrorCode.InvalidLayerName, ex.Code);
    }

    [Fact]
    public void LayerCreateValidatesNameAndReturnsTransactionShape()
    {
        var service = new LayerService();
        var descriptor = SampleDocument();
        var response = service.CreateLayer(new FakeLayerWriteAdapter(), descriptor, new LayerCreateRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "MCP_TEST_LAYER",
            ColorIndex = 3,
            IsPlottable = true
        }) with
        {
            Transaction = new WriteTransactionInfo
            {
                DocumentLocked = true,
                TransactionStarted = true,
                Committed = true
            }
        };

        var json = JsonSerializer.Serialize(response, JsonContractOptions.Default);

        Assert.True(response.Created);
        Assert.Equal("MCP_TEST_LAYER", response.Layer.Name);
        Assert.Contains("\"document_locked\":true", json);
        Assert.Contains("\"transaction_started\":true", json);
        Assert.Contains("\"color_index\":3", json);
    }

    [Fact]
    public void LayerSetCurrentReturnsPreviousAndCurrentLayersWithTransactionShape()
    {
        var service = new LayerService();
        var descriptor = SampleDocument();
        var response = service.SetCurrentLayer(new FakeLayerWriteAdapter(), descriptor, new LayerSetCurrentRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "C-ROAD-CNTR"
        }) with
        {
            Transaction = new WriteTransactionInfo
            {
                DocumentLocked = true,
                TransactionStarted = true,
                Committed = true
            }
        };

        var json = JsonSerializer.Serialize(response, JsonContractOptions.Default);

        Assert.True(response.Changed);
        Assert.Equal("0", response.PreviousLayer.Name);
        Assert.Equal("C-ROAD-CNTR", response.CurrentLayer.Name);
        Assert.True(response.CurrentLayer.IsCurrent);
        Assert.Contains("\"previous_layer\"", json);
        Assert.Contains("\"current_layer\"", json);
        Assert.Contains("\"document_locked\":true", json);
    }

    [Fact]
    public void LayerUpdateReturnsPreviousAndUpdatedLayersWithTransactionShape()
    {
        var service = new LayerService();
        var descriptor = SampleDocument();
        var response = service.UpdateLayer(new FakeLayerWriteAdapter(), descriptor, new LayerUpdateRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "C-ROAD-CNTR",
            NewName = "C-ROAD-CL",
            ColorIndex = 2,
            IsLocked = true,
            Description = "Centerline layer"
        }) with
        {
            Transaction = new WriteTransactionInfo
            {
                DocumentLocked = true,
                TransactionStarted = true,
                Committed = true
            }
        };

        var json = JsonSerializer.Serialize(response, JsonContractOptions.Default);

        Assert.True(response.Changed);
        Assert.Equal("C-ROAD-CNTR", response.PreviousLayer.Name);
        Assert.Equal("C-ROAD-CL", response.UpdatedLayer.Name);
        Assert.True(response.UpdatedLayer.IsLocked);
        Assert.Equal((short)2, response.UpdatedLayer.Color?.ColorIndex);
        Assert.Equal("Centerline layer", response.UpdatedLayer.Description);
        Assert.Contains("\"previous_layer\"", json);
        Assert.Contains("\"updated_layer\"", json);
        Assert.Contains("\"document_locked\":true", json);
    }

    [Fact]
    public void MissingLayerSetCurrentLocatorIsRejected()
    {
        var ex = Assert.Throws<LayerToolException>(() => LayerRequestValidator.ValidateLayerSetCurrent(new LayerSetCurrentRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1"
        }));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
    }

    [Fact]
    public void MissingLayerUpdateLocatorIsRejected()
    {
        var ex = Assert.Throws<LayerToolException>(() => LayerRequestValidator.ValidateLayerUpdate(new LayerUpdateRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            IsLocked = true
        }));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
    }

    [Fact]
    public void LayerUpdateRequiresAtLeastOneChange()
    {
        var ex = Assert.Throws<LayerToolException>(() => LayerRequestValidator.ValidateLayerUpdate(new LayerUpdateRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "C-ROAD-CNTR"
        }));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
    }

    [Fact]
    public void InvalidLayerUpdateColorIsRejected()
    {
        var ex = Assert.Throws<LayerToolException>(() => LayerRequestValidator.ValidateLayerUpdate(new LayerUpdateRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "C-ROAD-CNTR",
            ColorIndex = 0
        }));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
    }

    [Fact]
    public void InvalidLayerCreateColorIsRejected()
    {
        var ex = Assert.Throws<LayerToolException>(() => LayerRequestValidator.ValidateLayerCreate(new LayerCreateRequest
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "MCP_TEST_LAYER",
            ColorIndex = 300
        }));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
    }

    [Fact]
    public void DocumentTargetMustMatchRequestedDocument()
    {
        var service = new LayerService();
        var descriptor = SampleDocument();

        var ex = Assert.Throws<LayerToolException>(() => service.ListLayers(new FakeLayerAdapter([SampleLayer("0")]), descriptor, new LayerListRequest
        {
            InstanceId = "instance-1",
            DocumentId = "other-doc"
        }));

        Assert.Equal(ErrorCode.DocumentNotFound, ex.Code);
    }

    [Fact]
    public void MissingDocumentTargetIsAmbiguous()
    {
        var ex = Assert.Throws<LayerToolException>(() => LayerRequestValidator.ValidateDocumentTarget(
            "instance-1",
            "",
            SampleDocument()));

        Assert.Equal(ErrorCode.DocumentAmbiguous, ex.Code);
    }

    [Fact]
    public void LayerListSchemaIncludesDocumentTargetAndFilters()
    {
        var schema = LayerSchemas.LayerListInput();
        var properties = schema["properties"]!.AsObject();
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

        Assert.Contains("instance_id", required);
        Assert.Contains("document_id", required);
        Assert.Contains("name_contains", properties.Select(property => property.Key));
        Assert.Contains("is_plottable", properties.Select(property => property.Key));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void LayerCreateSchemaRequiresDocumentTargetAndName()
    {
        var schema = LayerSchemas.LayerCreateInput();
        var properties = schema["properties"]!.AsObject();
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

        Assert.Contains("instance_id", required);
        Assert.Contains("document_id", required);
        Assert.Contains("name", required);
        Assert.Contains("color_index", properties.Select(property => property.Key));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void LayerSetCurrentSchemaRequiresDocumentTargetAndSupportsLayerLocator()
    {
        var schema = LayerSchemas.LayerSetCurrentInput();
        var properties = schema["properties"]!.AsObject();
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

        Assert.Contains("instance_id", required);
        Assert.Contains("document_id", required);
        Assert.DoesNotContain("name", required);
        Assert.Contains("name", properties.Select(property => property.Key));
        Assert.Contains("handle", properties.Select(property => property.Key));
        Assert.Contains("object_reference", properties.Select(property => property.Key));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void LayerUpdateSchemaRequiresDocumentTargetAndSupportsPatchFields()
    {
        var schema = LayerSchemas.LayerUpdateInput();
        var properties = schema["properties"]!.AsObject();
        var required = schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

        Assert.Contains("instance_id", required);
        Assert.Contains("document_id", required);
        Assert.DoesNotContain("name", required);
        Assert.Contains("name", properties.Select(property => property.Key));
        Assert.Contains("handle", properties.Select(property => property.Key));
        Assert.Contains("object_reference", properties.Select(property => property.Key));
        Assert.Contains("new_name", properties.Select(property => property.Key));
        Assert.Contains("color_index", properties.Select(property => property.Key));
        Assert.Contains("is_on", properties.Select(property => property.Key));
        Assert.Contains("is_frozen", properties.Select(property => property.Key));
        Assert.Contains("is_locked", properties.Select(property => property.Key));
        Assert.Contains("is_plottable", properties.Select(property => property.Key));
        Assert.Contains("description", properties.Select(property => property.Key));
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void WritePipelineCommitsOnSuccess()
    {
        var scope = new FakeWriteScope("context");
        var pipeline = new WritePipeline<string>(new FakeWriteScopeFactory(scope));

        var result = pipeline.Execute(context => context.ToUpperInvariant());

        Assert.Equal("CONTEXT", result.Data);
        Assert.True(result.Transaction.DocumentLocked);
        Assert.True(result.Transaction.TransactionStarted);
        Assert.True(result.Transaction.Committed);
        Assert.False(result.Transaction.RolledBack);
    }

    [Fact]
    public void WritePipelineRollsBackOnError()
    {
        var scope = new FakeWriteScope("context");
        var pipeline = new WritePipeline<string>(new FakeWriteScopeFactory(scope));

        Assert.Throws<InvalidOperationException>(() => pipeline.Execute<string>(_ => throw new InvalidOperationException("boom")));

        Assert.False(scope.State.Committed);
        Assert.True(scope.State.RolledBack);
    }

    [Fact]
    public void WritePipelineDisposeRollsBackWhenTheOperationNeverCompleted()
    {
        var scope = new FakeWriteScope("context");

        using (scope)
        {
            // Abandoned without commit or rollback.
        }

        Assert.True(scope.Disposed);
        Assert.True(scope.State.RolledBack);
        Assert.False(scope.State.Committed);
    }

    [Fact]
    public void WritePipelineRollbackAfterCommitDoesNotUndoTheCommittedState()
    {
        var scope = new FakeWriteScope("context");
        var pipeline = new WritePipeline<string>(new FakeWriteScopeFactory(scope));

        pipeline.Execute(context => context);
        scope.Rollback();

        Assert.True(scope.State.Committed);
        Assert.False(scope.State.RolledBack);
        Assert.Equal(1, scope.CommitCalls);
    }

    [Fact]
    public void WritePipelineRollbackFailureIsReportedWithoutLosingTheOriginalCause()
    {
        var scope = new FakeWriteScope("context") { FailRollback = true };
        var pipeline = new WritePipeline<string>(new FakeWriteScopeFactory(scope));

        var error = Assert.Throws<LayerToolException>(
            () => pipeline.Execute<string>(_ => throw new LayerToolException(ErrorCode.LayerAlreadyExists, "layer already exists")));

        // Rollback failing is the more dangerous outcome, so that is the code the
        // caller sees. The reason the write failed must still be reachable.
        Assert.Equal(ErrorCode.RollbackFailed, error.Code);
        var cause = Assert.IsType<LayerToolException>(error.InnerException);
        Assert.Equal(ErrorCode.LayerAlreadyExists, cause.Code);
        Assert.Equal("layer already exists", cause.Message);
    }

    [Fact]
    public void WritePipelineDisposeDoesNotThrowWhenRollbackFails()
    {
        var scope = new FakeWriteScope("context") { FailRollback = true };

        // A throwing Dispose replaces whatever exception is already propagating,
        // which is how the real cause of a failed write got lost.
        var failure = Record.Exception(() => scope.Dispose());

        Assert.Null(failure);
        Assert.True(scope.Disposed);
    }

    [Fact]
    public void ManifestMetadataPublishesLayerToolsWithWriteCreate()
    {
        var tools = AutoCadToolCatalog.ListTools();
        var layerTools = tools.Where(tool => tool.Name.StartsWith("layer_", StringComparison.Ordinal)).ToArray();

        // Counts the layer tools rather than the whole catalog, so adding an
        // unrelated AutoCAD tool does not fail this test.
        Assert.Equal(5, layerTools.Length);
        Assert.Contains(tools, tool => tool.Name == AutoCadToolNames.LayerList);
        Assert.Contains(tools, tool => tool.Name == AutoCadToolNames.LayerGet);
        var create = Assert.Single(tools, tool => tool.Name == AutoCadToolNames.LayerCreate);
        var update = Assert.Single(tools, tool => tool.Name == AutoCadToolNames.LayerUpdate);
        var setCurrent = Assert.Single(tools, tool => tool.Name == AutoCadToolNames.LayerSetCurrent);

        Assert.All(tools, tool => Assert.Equal("AutoCAD", tool.Module));
        Assert.All(tools, tool => Assert.Equal(ToolRisk.Low, tool.Risk));
        Assert.All(tools, tool => Assert.False(tool.RequiresConfirmation));
        Assert.Equal(OperationClass.Write, create.OperationClass);
        Assert.Equal(RollbackSemantics.TransactionRollbackBeforeCommit, create.RollbackSemantics);
        Assert.Equal(OperationClass.Write, update.OperationClass);
        Assert.Equal(RollbackSemantics.TransactionRollbackBeforeCommit, update.RollbackSemantics);
        Assert.Equal(OperationClass.Write, setCurrent.OperationClass);
        Assert.Equal(RollbackSemantics.TransactionRollbackBeforeCommit, setCurrent.RollbackSemantics);
    }

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

    private static LayerInfo SampleLayer(string name) =>
        new()
        {
            Name = name,
            Handle = "A1",
            ObjectReference = SampleReference(name, "A1"),
            IsOn = true,
            IsFrozen = false,
            IsLocked = false,
            IsPlottable = true,
            Color = new LayerColorInfo
            {
                ColorIndex = 7,
                Method = "ByAci"
            },
            Linetype = "Continuous",
            Lineweight = "ByLineWeightDefault"
        };

    private static CadObjectReference SampleReference(string name, string handle) =>
        new()
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            DatabaseFingerprint = "fingerprint-1",
            Handle = handle,
            ObjectId = "(1)",
            Type = "LayerTableRecord",
            Name = name
        };

    private sealed class FakeLayerAdapter : ILayerAdapter
    {
        private readonly IReadOnlyList<LayerInfo> _layers;

        public FakeLayerAdapter(IReadOnlyList<LayerInfo> layers)
        {
            _layers = layers;
        }

        public IReadOnlyList<LayerInfo> ListLayers() => _layers;

        public LayerInfo? GetLayer(LayerLocator locator) =>
            _layers.FirstOrDefault(layer =>
                string.Equals(layer.Name, locator.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(layer.Handle, locator.Handle, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeLayerWriteAdapter : ILayerWriteAdapter
    {
        public LayerInfo CreateLayer(LayerCreateRequest request) =>
            SampleLayer(request.Name) with
            {
                Color = new LayerColorInfo
                {
                    ColorIndex = request.ColorIndex ?? 7,
                    Method = "ByAci"
                },
                IsPlottable = request.IsPlottable ?? true,
                Description = request.Description
            };

        public LayerSetCurrentResult SetCurrentLayer(LayerSetCurrentRequest request) =>
            new(
                PreviousLayer: SampleLayer("0") with { IsCurrent = false },
                CurrentLayer: SampleLayer(request.Name ?? request.ObjectReference?.Name ?? "C-ROAD-CNTR") with { IsCurrent = true },
                Changed: true);

        public LayerUpdateResult UpdateLayer(LayerUpdateRequest request)
        {
            var previous = SampleLayer(request.Name ?? request.ObjectReference?.Name ?? "C-ROAD-CNTR");
            var updated = previous with
            {
                Name = request.NewName ?? previous.Name,
                IsOn = request.IsOn ?? previous.IsOn,
                IsFrozen = request.IsFrozen ?? previous.IsFrozen,
                IsLocked = request.IsLocked ?? previous.IsLocked,
                IsPlottable = request.IsPlottable ?? previous.IsPlottable,
                Color = request.ColorIndex is { } colorIndex
                    ? new LayerColorInfo { ColorIndex = colorIndex, Method = "ByAci" }
                    : previous.Color,
                Description = request.Description ?? previous.Description
            };

            return new LayerUpdateResult(previous, updated, Changed: true);
        }
    }

    private sealed class FakeWriteScopeFactory : IWriteScopeFactory<string>
    {
        private readonly FakeWriteScope _scope;

        public FakeWriteScopeFactory(FakeWriteScope scope)
        {
            _scope = scope;
        }

        public IWriteScope<string> Begin() => _scope;
    }

    /// <summary>
    /// Mirrors <c>AutodeskWriteScope</c>, including its completed-once guard and
    /// its rollback-on-dispose. A fake that ignores those would pass while the
    /// real scope misbehaves, which is the failure this class exists to prevent.
    /// </summary>
    private sealed class FakeWriteScope : IWriteScope<string>
    {
        private bool _completed;

        public FakeWriteScope(string context)
        {
            Context = context;
            State = new WriteTransactionInfo
            {
                DocumentLocked = true,
                TransactionStarted = true
            };
        }

        public string Context { get; }
        public WriteTransactionInfo State { get; private set; }
        public bool Disposed { get; private set; }
        public bool FailRollback { get; init; }
        public int CommitCalls { get; private set; }

        public void Commit()
        {
            if (_completed)
            {
                return;
            }

            CommitCalls++;
            _completed = true;
            State = State with { Committed = true, RolledBack = false };
        }

        public void Rollback()
        {
            if (_completed)
            {
                return;
            }

            if (FailRollback)
            {
                throw new LayerToolException(ErrorCode.RollbackFailed, "Could not roll back the AutoCAD transaction: abort failed.");
            }

            _completed = true;
            State = State with { RolledBack = true, Committed = false };
        }

        public void Dispose()
        {
            try
            {
                if (!_completed)
                {
                    Rollback();
                }
            }
            catch (LayerToolException)
            {
                // Mirrors the real scope: Dispose never throws.
            }

            Disposed = true;
        }
    }
}
