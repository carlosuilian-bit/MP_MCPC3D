using System.Text.Json;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;
using Civil3D.HybridMcp.Server;
using ModelContextProtocol.Client;
using McpCallToolRequestParams = ModelContextProtocol.Protocol.CallToolRequestParams;
using McpCallToolResult = ModelContextProtocol.Protocol.CallToolResult;
using McpImplementation = ModelContextProtocol.Protocol.Implementation;
using McpListToolsRequestParams = ModelContextProtocol.Protocol.ListToolsRequestParams;

namespace Civil3D.HybridMcp.Integration.Tests;

public sealed class ServerFakeBridgeTests
{
    [Fact]
    public async Task HandshakeDiscoversToolManifestFromFakeBridge()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-manifest");
        var server = CreateServer(temp.Path);

        var manifest = await server.GetToolManifestAsync(new EndpointSelectorCriteria { InstanceId = bridge.Descriptor.InstanceId });

        Assert.Equal(49, manifest.Tools.Count);
        Assert.Contains(manifest.Tools, tool => tool.Name == "civil3d_ping");
        Assert.Contains(manifest.Tools, tool => tool.Name == "layer_list");
        Assert.Contains(manifest.Tools, tool => tool.Name == "layer_get");
        Assert.Contains(manifest.Tools, tool => tool.Name == "layer_create" && tool.OperationClass == OperationClass.Write);
        Assert.Contains(manifest.Tools, tool => tool.Name == "layer_update" && tool.OperationClass == OperationClass.Write);
        Assert.Contains(manifest.Tools, tool => tool.Name == "layer_set_current" && tool.OperationClass == OperationClass.Write);
        Assert.Contains(manifest.Tools, tool => tool.Name == "alignment_list");
        Assert.Contains(manifest.Tools, tool => tool.Name == "surface_elevation_at_xy");
        Assert.Contains(manifest.Tools, tool => tool.Name == "cogo_get");
        Assert.Contains(manifest.Tools, tool => tool.Name == "survey_bearing_to_azimuth");
        Assert.Contains(manifest.Tools, tool => tool.Name == "survey_point_by_azimuth");
        Assert.Contains(manifest.Tools, tool => tool.Name == "survey_fbk_dry_run");
        Assert.Contains(manifest.Tools, tool => tool.Name == "survey_database_status");
        Assert.Contains(manifest.Tools, tool => tool.Name == "survey_working_folder_get");
        Assert.Contains(manifest.Tools, tool => tool.Name == "survey_project_list");
        Assert.Contains(manifest.Tools, tool => tool.Name == "survey_network_list");
        Assert.Contains(manifest.Tools, tool => tool.Name == "profile_list");
        Assert.Contains(manifest.Tools, tool => tool.Name == "profile_get");
        Assert.Contains(manifest.Tools, tool => tool.Name == "profile_elevation_at_station");
        Assert.Contains(manifest.Tools, tool => tool.Name == "corridor_list");
        Assert.Contains(manifest.Tools, tool => tool.Name == "corridor_get");
        Assert.Contains(manifest.Tools, tool => tool.Name == "list_dialogs");
        Assert.Contains(manifest.Tools, tool =>
            tool.Name == "roadway_station_offset_batch" &&
            tool.Module == "Roadway" &&
            tool.OperationClass == OperationClass.Read);
        Assert.Contains(manifest.Tools, tool =>
            tool.Name == "roadway_propertyset_write" &&
            tool.Module == "Roadway" &&
            tool.RequiresConfirmation);
        Assert.Contains(manifest.Tools, tool => tool.Name == "dialog_decision_list");
        Assert.Contains(manifest.Tools, tool => tool.Name == "dialog_decision_set");
        Assert.Contains(manifest.Tools, tool => tool.Name == "dismiss_dialog" && tool.OperationClass == OperationClass.Write);
        Assert.Contains(manifest.Tools, tool => tool.Name == "object_get" && tool.OperationClass == OperationClass.Read);
        Assert.Contains(manifest.Tools, tool => tool.Name == "object_properties" && tool.OperationClass == OperationClass.Read);
        Assert.Contains(manifest.Tools, tool => tool.Name == "propertyset_definition_list");
        Assert.Contains(manifest.Tools, tool => tool.Name == "propertyset_list");
        Assert.Contains(manifest.Tools, tool => tool.Name == "propertyset_get");
        Assert.Contains(manifest.Tools, tool => tool.Name == "propertyset_find" && tool.OperationClass == OperationClass.Read);
        Assert.Contains(manifest.Tools, tool =>
            tool.Name == "propertyset_set" &&
            tool.OperationClass == OperationClass.Write &&
            tool.RequiresConfirmation &&
            tool.Risk == ToolRisk.Medium);
        Assert.Contains(manifest.Tools, tool =>
            tool.Name == "propertyset_numeric_test_create" &&
            tool.OperationClass == OperationClass.Write &&
            tool.RollbackSemantics == RollbackSemantics.TransactionRollbackBeforeCommit);
        Assert.True(bridge.Descriptor.Capabilities.FeatureFlags["survey_fbk_dry_run_enabled"]);
        Assert.True(bridge.Descriptor.Capabilities.FeatureFlags["survey_database_status_enabled"]);
        Assert.True(bridge.Descriptor.Capabilities.FeatureFlags["survey_working_folder_get_enabled"]);
        Assert.True(bridge.Descriptor.Capabilities.FeatureFlags["survey_project_list_enabled"]);
        Assert.True(bridge.Descriptor.Capabilities.FeatureFlags["survey_network_list_enabled"]);
    }

    [Fact]
    public async Task ServerExecutesInitialFourReadTools()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-tools");
        var server = CreateServer(temp.Path);
        var criteria = new EndpointSelectorCriteria { InstanceId = bridge.Descriptor.InstanceId };

        var ping = await server.PingAsync(criteria);
        var status = await server.StatusAsync(criteria);
        var documents = await server.DocumentListAsync(criteria);
        var drawing = await server.DrawingInfoAsync(criteria);

        Assert.True(ping.Success);
        Assert.True(status.Success);
        Assert.True(documents.Success);
        Assert.True(drawing.Success);
        Assert.NotNull(ping.Data);
        Assert.NotNull(status.Data);
        Assert.NotNull(documents.Data);
        Assert.NotNull(drawing.Data);

        var layerList = await server.ExecuteToolAsync("layer_list", JsonContractOptions.ToJsonNode(new LayerListRequest
        {
            InstanceId = bridge.Descriptor.InstanceId,
            DocumentId = bridge.Descriptor.ActiveDocument!.DocumentId
        }), criteria);
        Assert.True(layerList.Success);
    }

    [Fact]
    public async Task ServerRejectsAmbiguousInstanceWhenNoInstanceIdProvided()
    {
        using var temp = TempDirectory.Create();
        await using var first = FakeBridgeHost.Start(temp.Path, "instance-a");
        await using var second = FakeBridgeHost.Start(temp.Path, "instance-b");
        var server = CreateServer(temp.Path);

        var ex = await Assert.ThrowsAsync<HybridMcpServerException>(() => server.PingAsync());
        var selected = await server.PingAsync(new EndpointSelectorCriteria { InstanceId = first.Descriptor.InstanceId });

        Assert.Equal(ErrorCode.InstanceAmbiguous, ex.Code);
        Assert.True(selected.Success);
    }

    [Fact]
    public async Task ServerRejectsProtocolMajorMismatch()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-v2", new ProtocolVersion(2, 0, 0));
        var server = CreateServer(temp.Path);

        var ex = await Assert.ThrowsAsync<HybridMcpServerException>(
            () => server.PingAsync(new EndpointSelectorCriteria { InstanceId = bridge.Descriptor.InstanceId }));

        Assert.Equal(ErrorCode.ProtocolIncompatible, ex.Code);
    }

    [Fact]
    public async Task ServerRejectsStaleEndpoint()
    {
        using var temp = TempDirectory.Create();
        var options = new EndpointRegistryOptions
        {
            DirectoryPath = temp.Path,
            StaleAfter = TimeSpan.FromMilliseconds(10)
        };
        var descriptor = bridgeDescriptor("stale-instance") with
        {
            LastHeartbeatUtc = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5)
        };
        new EndpointRegistryWriter(options).Write(descriptor);
        var server = CreateServer(options);

        var ex = await Assert.ThrowsAsync<HybridMcpServerException>(() => server.PingAsync());

        Assert.Equal(ErrorCode.BridgeUnavailable, ex.Code);
    }

    [Fact]
    public async Task ServerTimesOutDelayedBridgeResponse()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(
            temp.Path,
            "instance-timeout",
            delayMs: 250,
            toolTimeout: TimeSpan.FromMilliseconds(50));
        var server = CreateServer(temp.Path);

        var ex = await Assert.ThrowsAsync<HybridMcpServerException>(
            () => server.PingAsync(new EndpointSelectorCriteria { InstanceId = bridge.Descriptor.InstanceId }));

        Assert.Equal(ErrorCode.Timeout, ex.Code);
    }

    [Fact]
    public async Task ServerCanReconnectAfterBridgeDisconnect()
    {
        using var temp = TempDirectory.Create();
        var server = CreateServer(temp.Path);

        await using (var first = FakeBridgeHost.Start(temp.Path, "instance-first"))
        {
            var ping = await server.PingAsync(new EndpointSelectorCriteria { InstanceId = first.Descriptor.InstanceId });
            Assert.True(ping.Success);
        }

        await using (var second = FakeBridgeHost.Start(temp.Path, "instance-second"))
        {
            var ping = await server.PingAsync(new EndpointSelectorCriteria { InstanceId = second.Descriptor.InstanceId });
            Assert.True(ping.Success);
        }
    }

    [Fact]
    public async Task McpAdapterMapsBridgeManifestToToolSchemas()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-mcp-schema");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);

        var result = await surface.ListToolsAsync();

        Assert.Equal(49, result.Tools.Count);
        var ping = Assert.Single(result.Tools, tool => tool.Name == "civil3d_ping");
        Assert.False(ping.InputSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("PingResponse", ping.OutputSchema!.Value.GetProperty("title").GetString());
        Assert.Equal("Core", ping.Meta!["civil3d_hybrid_mcp.module"]!.GetValue<string>());
        Assert.True(ping.Annotations!.ReadOnlyHint);

        var layerList = Assert.Single(result.Tools, tool => tool.Name == "layer_list");
        var layerProperties = layerList.InputSchema.GetProperty("properties");
        Assert.True(layerProperties.TryGetProperty("instance_id", out _));
        Assert.True(layerProperties.TryGetProperty("document_id", out _));
        Assert.True(layerProperties.TryGetProperty("name_contains", out _));
        Assert.Equal("AutoCAD", layerList.Meta!["civil3d_hybrid_mcp.module"]!.GetValue<string>());

        var alignmentStationOffset = Assert.Single(result.Tools, tool => tool.Name == "alignment_station_offset");
        var civilProperties = alignmentStationOffset.InputSchema.GetProperty("properties");
        Assert.True(civilProperties.TryGetProperty("x", out _));
        Assert.True(civilProperties.TryGetProperty("y", out _));
        Assert.Equal("Civil3D", alignmentStationOffset.Meta!["civil3d_hybrid_mcp.module"]!.GetValue<string>());

        var bearingToAzimuth = Assert.Single(result.Tools, tool => tool.Name == "survey_bearing_to_azimuth");
        var surveyProperties = bearingToAzimuth.InputSchema.GetProperty("properties");
        Assert.True(surveyProperties.TryGetProperty("bearing", out _));
        Assert.Equal("Survey", bearingToAzimuth.Meta!["civil3d_hybrid_mcp.module"]!.GetValue<string>());

        var fbkDryRun = Assert.Single(result.Tools, tool => tool.Name == "survey_fbk_dry_run");
        var fbkProperties = fbkDryRun.InputSchema.GetProperty("properties");
        Assert.True(fbkProperties.TryGetProperty("content", out _));
        Assert.True(fbkProperties.TryGetProperty("source_name", out _));
        Assert.Equal("Survey", fbkDryRun.Meta!["civil3d_hybrid_mcp.module"]!.GetValue<string>());

        var databaseStatus = Assert.Single(result.Tools, tool => tool.Name == "survey_database_status");
        Assert.Empty(databaseStatus.InputSchema.GetProperty("required").EnumerateArray());
        Assert.False(databaseStatus.InputSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("Survey", databaseStatus.Meta!["civil3d_hybrid_mcp.module"]!.GetValue<string>());

        var workingFolderGet = Assert.Single(result.Tools, tool => tool.Name == "survey_working_folder_get");
        Assert.Empty(workingFolderGet.InputSchema.GetProperty("required").EnumerateArray());
        Assert.False(workingFolderGet.InputSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("Survey", workingFolderGet.Meta!["civil3d_hybrid_mcp.module"]!.GetValue<string>());

        var projectList = Assert.Single(result.Tools, tool => tool.Name == "survey_project_list");
        Assert.Empty(projectList.InputSchema.GetProperty("required").EnumerateArray());
        Assert.False(projectList.InputSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal("Survey", projectList.Meta!["civil3d_hybrid_mcp.module"]!.GetValue<string>());

        var profileList = Assert.Single(result.Tools, tool => tool.Name == "profile_list");
        Assert.True(profileList.InputSchema.GetProperty("properties").TryGetProperty("alignment_name", out _));
        Assert.Equal("Civil3D", profileList.Meta!["civil3d_hybrid_mcp.module"]!.GetValue<string>());

        var profileElevation = Assert.Single(result.Tools, tool => tool.Name == "profile_elevation_at_station");
        Assert.Contains(
            profileElevation.InputSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()),
            value => value == "station");

        var networkList = Assert.Single(result.Tools, tool => tool.Name == "survey_network_list");
        Assert.Empty(networkList.InputSchema.GetProperty("required").EnumerateArray());
        Assert.False(networkList.InputSchema.GetProperty("additionalProperties").GetBoolean());
        Assert.True(networkList.InputSchema.GetProperty("properties").TryGetProperty("project_name", out _));
        Assert.Equal("Survey", networkList.Meta!["civil3d_hybrid_mcp.module"]!.GetValue<string>());

        var numericPropertySet = Assert.Single(result.Tools, tool => tool.Name == "propertyset_numeric_test_create");
        Assert.False(numericPropertySet.Annotations!.ReadOnlyHint);
        Assert.False(numericPropertySet.Annotations!.DestructiveHint);
        Assert.Equal("Civil3D", numericPropertySet.Meta!["civil3d_hybrid_mcp.module"]!.GetValue<string>());
        Assert.Equal("Write", numericPropertySet.Meta!["civil3d_hybrid_mcp.operation_class"]!.GetValue<string>());
        var numericPropertySetSchema = numericPropertySet.InputSchema;
        Assert.Contains(
            numericPropertySetSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString()),
            value => value == "numeric_value");
        Assert.True(numericPropertySetSchema.GetProperty("properties").TryGetProperty("definition_name", out _));
        Assert.True(numericPropertySetSchema.GetProperty("properties").TryGetProperty("property_name", out _));
        Assert.True(numericPropertySetSchema.GetProperty("properties").TryGetProperty("target_kind", out _));
    }

    [Fact]
    public async Task McpAdapterReturnsBridgeUnavailableWhenNoBridgeIsRunning()
    {
        using var temp = TempDirectory.Create();
        var surface = CreateMcpSurface(temp.Path);

        var tools = await surface.ListToolsAsync();
        var result = await surface.CallToolAsync("civil3d_ping", null);

        Assert.Empty(tools.Tools);
        AssertMcpError(result, "BRIDGE_UNAVAILABLE");
    }

    [Fact]
    public async Task McpAdapterReconnectsWhenBridgeAppearsLater()
    {
        using var temp = TempDirectory.Create();
        var surface = CreateMcpSurface(temp.Path);

        AssertMcpError(await surface.CallToolAsync("civil3d_ping", null), "BRIDGE_UNAVAILABLE");

        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-late");
        var tools = await surface.ListToolsAsync();
        var result = await surface.CallToolAsync("civil3d_ping", null);

        Assert.Equal(49, tools.Tools.Count);
        AssertMcpSuccess(result);
        Assert.Equal(bridge.Descriptor.InstanceId, Structured(result).GetProperty("data").GetProperty("instanceId").GetString());
    }

    [Fact]
    public async Task McpAdapterReportsDisconnectAndRecoversWithNewBridge()
    {
        using var temp = TempDirectory.Create();
        var surface = CreateMcpSurface(temp.Path);

        await using (FakeBridgeHost.Start(temp.Path, "instance-before-disconnect"))
        {
            AssertMcpSuccess(await surface.CallToolAsync("civil3d_ping", null));
        }

        AssertMcpError(await surface.CallToolAsync("civil3d_ping", null), "BRIDGE_UNAVAILABLE");

        await using (FakeBridgeHost.Start(temp.Path, "instance-after-disconnect"))
        {
            AssertMcpSuccess(await surface.CallToolAsync("civil3d_ping", null));
        }
    }

    [Fact]
    public async Task McpAdapterRejectsAmbiguousInstances()
    {
        using var temp = TempDirectory.Create();
        await using var first = FakeBridgeHost.Start(temp.Path, "instance-mcp-a");
        await using var second = FakeBridgeHost.Start(temp.Path, "instance-mcp-b");
        var surface = CreateMcpSurface(temp.Path);

        var result = await surface.CallToolAsync("civil3d_ping", null);

        AssertMcpError(result, "INSTANCE_AMBIGUOUS");
    }

    [Fact]
    public async Task McpAdapterReturnsNoToolsWhenInstancesAreAmbiguous()
    {
        using var temp = TempDirectory.Create();
        await using var first = FakeBridgeHost.Start(temp.Path, "instance-mcp-list-a");
        await using var second = FakeBridgeHost.Start(temp.Path, "instance-mcp-list-b");
        var surface = CreateMcpSurface(temp.Path);

        var result = await surface.ListToolsAsync();

        Assert.Empty(result.Tools);
    }

    [Fact]
    public async Task McpAdapterPropagatesProtocolIncompatibility()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-mcp-v2", new ProtocolVersion(2, 0, 0));
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);

        var result = await surface.CallToolAsync("civil3d_ping", null);

        AssertMcpError(result, "PROTOCOL_INCOMPATIBLE");
    }

    [Fact]
    public async Task McpAdapterRejectsMalformedToolRequest()
    {
        using var temp = TempDirectory.Create();
        var surface = CreateMcpSurface(temp.Path);

        var result = await surface.CallToolAsync("", null);

        AssertMcpError(result, "INVALID_REQUEST");
    }

    [Fact]
    public async Task McpAdapterPropagatesStructuredBridgeToolErrors()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(
            temp.Path,
            "instance-tool-error",
            forcedErrors: new Dictionary<string, ErrorEnvelope>(StringComparer.OrdinalIgnoreCase)
            {
                ["drawing_info"] = new ErrorEnvelope
                {
                    Code = ErrorCode.DocumentNotFound,
                    Message = "No active drawing."
                }
            });
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);

        var result = await surface.CallToolAsync("drawing_info", null);

        AssertMcpError(result, "DOCUMENT_NOT_FOUND");
        Assert.Equal("DocumentNotFound", Structured(result).GetProperty("error").GetProperty("internalCode").GetString());
    }

    [Fact]
    public async Task McpAdapterCallsLayerListWithDocumentTarget()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-layer-list");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);

        var result = await surface.CallToolAsync("layer_list", LayerArgs(bridge));

        AssertMcpSuccess(result);
        var data = Structured(result).GetProperty("data");
        Assert.Equal(3, data.GetProperty("count").GetInt32());
        Assert.Equal("0", data.GetProperty("layers")[0].GetProperty("name").GetString());
        Assert.True(data.GetProperty("layers")[0].GetProperty("is_current").GetBoolean());
    }

    [Fact]
    public async Task McpAdapterCallsLayerGetByNameWithDocumentTarget()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-layer-get");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);
        var args = LayerArgs(bridge);
        args["name"] = JsonSerializer.SerializeToElement("Defpoints");

        var result = await surface.CallToolAsync("layer_get", args);

        AssertMcpSuccess(result);
        var layer = Structured(result).GetProperty("data").GetProperty("layer");
        Assert.Equal("Defpoints", layer.GetProperty("name").GetString());
        Assert.False(layer.GetProperty("is_plottable").GetBoolean());
    }

    [Fact]
    public async Task McpAdapterCallsObjectGetByHandle()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-object-get");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);
        var args = LayerArgs(bridge);
        args["handle"] = JsonSerializer.SerializeToElement("B2");

        var result = await surface.CallToolAsync("object_get", args);

        AssertMcpSuccess(result);
        var target = Structured(result).GetProperty("data").GetProperty("object");
        Assert.Equal("B2", target.GetProperty("handle").GetString());
        Assert.Equal("AcDbLine", target.GetProperty("object_class").GetString());
        Assert.True(target.GetProperty("is_entity").GetBoolean());
        Assert.Equal("0", target.GetProperty("entity").GetProperty("layer").GetString());
        Assert.Equal(3, target.GetProperty("class_hierarchy").GetArrayLength());
    }

    [Fact]
    public async Task McpAdapterListsBlockReferencesWithSnakeCaseFields()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-block-references");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);
        var args = LayerArgs(bridge);
        args["block_name"] = JsonSerializer.SerializeToElement("PLACA");

        var result = await surface.CallToolAsync("block_reference_list", args);

        AssertMcpSuccess(result);
        var data = Structured(result).GetProperty("data");
        Assert.Equal(4146, data.GetProperty("scanned_count").GetInt32());
        Assert.False(data.GetProperty("truncated").GetBoolean());

        // The case the block tools exist for: a dynamic insert reported by its
        // real name while pointing at an anonymous definition.
        var reference = data.GetProperty("references")[0];
        Assert.Equal("PLACA", reference.GetProperty("block_name").GetString());
        Assert.Equal("*U12", reference.GetProperty("definition_name").GetString());
        Assert.True(reference.GetProperty("is_dynamic").GetBoolean());
        Assert.Equal(1.5707963267948966, reference.GetProperty("rotation_radians").GetDouble());
        Assert.Equal(1, reference.GetProperty("scale_x").GetDouble());
    }

    [Fact]
    public async Task McpAdapterReadsBlockAttributesIncludingConstants()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-block-attributes");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);
        var args = LayerArgs(bridge);
        args["handle"] = JsonSerializer.SerializeToElement("2B1");

        var result = await surface.CallToolAsync("block_get_attributes", args);

        AssertMcpSuccess(result);
        var attributes = Structured(result).GetProperty("data").GetProperty("attributes");
        Assert.Equal(2, attributes.GetArrayLength());
        var constant = attributes[1];
        Assert.Equal("TIPO", constant.GetProperty("tag").GetString());
        Assert.True(constant.GetProperty("constant").GetBoolean());
        Assert.Equal(JsonValueKind.Null, constant.GetProperty("handle").ValueKind);
    }

    [Fact]
    public async Task McpAdapterReportsObjectNotFoundForAnUnknownHandle()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-object-missing");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);
        var args = LayerArgs(bridge);
        args["handle"] = JsonSerializer.SerializeToElement("DEADBEEF");

        var result = await surface.CallToolAsync("object_get", args);

        var error = Structured(result).GetProperty("error");
        Assert.Equal("OBJECT_NOT_FOUND", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task McpAdapterCallsObjectPropertiesAndHidesUnreadableValuesByDefault()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-object-properties");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);
        var args = LayerArgs(bridge);
        args["handle"] = JsonSerializer.SerializeToElement("B2");

        var result = await surface.CallToolAsync("object_properties", args);

        AssertMcpSuccess(result);
        var data = Structured(result).GetProperty("data");
        Assert.Equal("AcDbLine", data.GetProperty("object_class").GetString());
        Assert.Equal(2, data.GetProperty("count").GetInt32());
        Assert.False(data.GetProperty("truncated").GetBoolean());

        var length = data.GetProperty("properties")[0];
        Assert.Equal("Length", length.GetProperty("name").GetString());
        Assert.Equal(111.80339887498948, length.GetProperty("numeric_value").GetDouble());

        var withUnreadable = LayerArgs(bridge);
        withUnreadable["handle"] = JsonSerializer.SerializeToElement("B2");
        withUnreadable["include_unreadable"] = JsonSerializer.SerializeToElement(true);

        var full = await surface.CallToolAsync("object_properties", withUnreadable);

        AssertMcpSuccess(full);
        var fullData = Structured(full).GetProperty("data");
        Assert.Equal(3, fullData.GetProperty("count").GetInt32());
        Assert.Equal("eNotApplicable", fullData.GetProperty("properties")[2].GetProperty("error").GetString());
    }

    [Fact]
    public async Task McpAdapterCallsLayerCreateWithDocumentTarget()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-layer-create");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);
        var args = LayerArgs(bridge);
        args["name"] = JsonSerializer.SerializeToElement("MCP_TEST_LAYER");
        args["color_index"] = JsonSerializer.SerializeToElement(3);

        var result = await surface.CallToolAsync("layer_create", args);

        AssertMcpSuccess(result);
        var data = Structured(result).GetProperty("data");
        Assert.True(data.GetProperty("created").GetBoolean());
        Assert.Equal("MCP_TEST_LAYER", data.GetProperty("layer").GetProperty("name").GetString());
        Assert.True(data.GetProperty("transaction").GetProperty("document_locked").GetBoolean());
        Assert.True(data.GetProperty("transaction").GetProperty("committed").GetBoolean());
    }

    [Fact]
    public async Task McpAdapterCallsLayerSetCurrentWithDocumentTarget()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-layer-current");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);
        var args = LayerArgs(bridge);
        args["name"] = JsonSerializer.SerializeToElement("Defpoints");

        var result = await surface.CallToolAsync("layer_set_current", args);

        AssertMcpSuccess(result);
        var data = Structured(result).GetProperty("data");
        Assert.True(data.GetProperty("changed").GetBoolean());
        Assert.Equal("0", data.GetProperty("previous_layer").GetProperty("name").GetString());
        Assert.Equal("Defpoints", data.GetProperty("current_layer").GetProperty("name").GetString());
        Assert.True(data.GetProperty("transaction").GetProperty("document_locked").GetBoolean());
        Assert.True(data.GetProperty("transaction").GetProperty("committed").GetBoolean());

        var listed = await surface.CallToolAsync("layer_list", LayerArgs(bridge));
        AssertMcpSuccess(listed);
        var current = Structured(listed)
            .GetProperty("data")
            .GetProperty("layers")
            .EnumerateArray()
            .Single(layer => layer.GetProperty("is_current").GetBoolean());
        Assert.Equal("Defpoints", current.GetProperty("name").GetString());
    }

    [Fact]
    public async Task McpAdapterCallsLayerUpdateWithDocumentTarget()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-layer-update");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);
        var args = LayerArgs(bridge);
        args["name"] = JsonSerializer.SerializeToElement("Defpoints");
        args["new_name"] = JsonSerializer.SerializeToElement("MCP_UPDATED_LAYER");
        args["color_index"] = JsonSerializer.SerializeToElement(2);
        args["is_locked"] = JsonSerializer.SerializeToElement(true);
        args["description"] = JsonSerializer.SerializeToElement("Updated by fake bridge test");

        var result = await surface.CallToolAsync("layer_update", args);

        AssertMcpSuccess(result);
        var data = Structured(result).GetProperty("data");
        Assert.True(data.GetProperty("changed").GetBoolean());
        Assert.Equal("Defpoints", data.GetProperty("previous_layer").GetProperty("name").GetString());
        var updated = data.GetProperty("updated_layer");
        Assert.Equal("MCP_UPDATED_LAYER", updated.GetProperty("name").GetString());
        Assert.True(updated.GetProperty("is_locked").GetBoolean());
        Assert.Equal(2, updated.GetProperty("color").GetProperty("color_index").GetInt16());
        Assert.Equal("Updated by fake bridge test", updated.GetProperty("description").GetString());
        Assert.True(data.GetProperty("transaction").GetProperty("document_locked").GetBoolean());
        Assert.True(data.GetProperty("transaction").GetProperty("committed").GetBoolean());

        var getArgs = LayerArgs(bridge);
        getArgs["name"] = JsonSerializer.SerializeToElement("MCP_UPDATED_LAYER");
        var readBack = await surface.CallToolAsync("layer_get", getArgs);
        AssertMcpSuccess(readBack);
        Assert.Equal("MCP_UPDATED_LAYER", Structured(readBack).GetProperty("data").GetProperty("layer").GetProperty("name").GetString());
    }

    [Fact]
    public async Task McpAdapterPropagatesLayerAlreadyExistsForCreate()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-layer-create-duplicate");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);
        var args = LayerArgs(bridge);
        args["name"] = JsonSerializer.SerializeToElement("0");

        var result = await surface.CallToolAsync("layer_create", args);

        AssertMcpError(result, "LAYER_ALREADY_EXISTS");
    }

    [Fact]
    public async Task McpAdapterPropagatesLayerNotFound()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-layer-missing");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);
        var args = LayerArgs(bridge);
        args["name"] = JsonSerializer.SerializeToElement("DOES_NOT_EXIST");

        var result = await surface.CallToolAsync("layer_get", args);

        AssertMcpError(result, "LAYER_NOT_FOUND");
    }

    [Fact]
    public async Task McpAdapterPropagatesDocumentNotFoundForLayerTool()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-layer-doc-missing");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);
        var args = LayerArgs(bridge);
        args["document_id"] = JsonSerializer.SerializeToElement("missing-document");

        var result = await surface.CallToolAsync("layer_list", args);

        AssertMcpError(result, "DOCUMENT_NOT_FOUND");
    }

    [Fact]
    public async Task McpAdapterCallsCivilReadToolsWithDocumentTarget()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-civil-read");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);
        var args = CivilArgs(bridge);

        var alignments = await surface.CallToolAsync("alignment_list", args);
        AssertMcpSuccess(alignments);
        var alignment = Structured(alignments).GetProperty("data").GetProperty("alignments")[0];
        Assert.Equal("MAIN_ROAD", alignment.GetProperty("name").GetString());

        var stationArgs = CivilArgs(bridge);
        stationArgs["name"] = JsonSerializer.SerializeToElement("MAIN_ROAD");
        stationArgs["x"] = JsonSerializer.SerializeToElement(1000);
        stationArgs["y"] = JsonSerializer.SerializeToElement(2000);
        var station = await surface.CallToolAsync("alignment_station_offset", stationArgs);
        AssertMcpSuccess(station);
        Assert.Equal(100, Structured(station).GetProperty("data").GetProperty("station").GetDouble());

        var surfaces = await surface.CallToolAsync("surface_list", args);
        AssertMcpSuccess(surfaces);
        Assert.Equal("EG", Structured(surfaces).GetProperty("data").GetProperty("surfaces")[0].GetProperty("name").GetString());

        var elevationArgs = CivilArgs(bridge);
        elevationArgs["name"] = JsonSerializer.SerializeToElement("EG");
        elevationArgs["x"] = JsonSerializer.SerializeToElement(1000);
        elevationArgs["y"] = JsonSerializer.SerializeToElement(2000);
        var elevation = await surface.CallToolAsync("surface_elevation_at_xy", elevationArgs);
        AssertMcpSuccess(elevation);
        Assert.Equal(101.5, Structured(elevation).GetProperty("data").GetProperty("elevation").GetDouble());

        var cogo = await surface.CallToolAsync("cogo_list", args);
        AssertMcpSuccess(cogo);
        Assert.Equal(1, Structured(cogo).GetProperty("data").GetProperty("points")[0].GetProperty("point_number").GetInt64());

        var cogoGetArgs = CivilArgs(bridge);
        cogoGetArgs["point_number"] = JsonSerializer.SerializeToElement(1);
        var cogoGet = await surface.CallToolAsync("cogo_get", cogoGetArgs);
        AssertMcpSuccess(cogoGet);
        Assert.Equal(1000, Structured(cogoGet).GetProperty("data").GetProperty("point").GetProperty("easting").GetDouble());
    }

    [Fact]
    public async Task McpAdapterCreatesNumericPropertySetTestWithTransactionMetadata()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-propertyset-numeric");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);
        var args = CivilArgs(bridge);
        args["definition_name"] = JsonSerializer.SerializeToElement("MCP_NUMERIC_TEST_FAKE");
        args["property_name"] = JsonSerializer.SerializeToElement("NumericValue");
        args["numeric_value"] = JsonSerializer.SerializeToElement(123.456);

        var result = await surface.CallToolAsync("propertyset_numeric_test_create", args);

        AssertMcpSuccess(result);
        var data = Structured(result).GetProperty("data");
        Assert.Equal("MCP_NUMERIC_TEST_FAKE", data.GetProperty("definition_name").GetString());
        Assert.Equal("db_point", data.GetProperty("target_kind").GetString());
        Assert.Equal("TMP1", data.GetProperty("object_handle").GetString());
        var property = data.GetProperty("property_set").GetProperty("properties")[0];
        Assert.Equal("NumericValue", property.GetProperty("name").GetString());
        Assert.Equal("Real", property.GetProperty("data_type").GetString());
        Assert.Equal(123.456, property.GetProperty("numeric_value").GetDouble(), precision: 3);
        Assert.Equal("123.456", property.GetProperty("value").GetString());
        Assert.True(data.GetProperty("transaction").GetProperty("document_locked").GetBoolean());
        Assert.True(data.GetProperty("transaction").GetProperty("transaction_started").GetBoolean());
        Assert.True(data.GetProperty("transaction").GetProperty("committed").GetBoolean());
        Assert.False(data.GetProperty("transaction").GetProperty("rolled_back").GetBoolean());

        var readBackArgs = CivilArgs(bridge);
        readBackArgs["handle"] = JsonSerializer.SerializeToElement(data.GetProperty("object_handle").GetString());
        readBackArgs["name"] = JsonSerializer.SerializeToElement("MCP_NUMERIC_TEST_FAKE");
        var readBack = await surface.CallToolAsync("propertyset_get", readBackArgs);
        AssertMcpSuccess(readBack);
        var readBackProperty = Structured(readBack)
            .GetProperty("data")
            .GetProperty("property_set")
            .GetProperty("properties")[0];
        Assert.Equal(123.456, readBackProperty.GetProperty("numeric_value").GetDouble(), precision: 3);
    }

    [Fact]
    public async Task McpAdapterCallsSurveyGeometryToolsWithoutDocumentTarget()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-survey-geometry");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);

        var bearing = await surface.CallToolAsync("survey_bearing_to_azimuth", new Dictionary<string, JsonElement>
        {
            ["bearing"] = JsonSerializer.SerializeToElement("S 45 0 0 E")
        });
        AssertMcpSuccess(bearing);
        Assert.Equal(135, Structured(bearing).GetProperty("data").GetProperty("azimuth_degrees").GetDouble());

        var distance = await surface.CallToolAsync("survey_distance", new Dictionary<string, JsonElement>
        {
            ["start_x"] = JsonSerializer.SerializeToElement(1000),
            ["start_y"] = JsonSerializer.SerializeToElement(2000),
            ["end_x"] = JsonSerializer.SerializeToElement(1010),
            ["end_y"] = JsonSerializer.SerializeToElement(2010)
        });
        AssertMcpSuccess(distance);
        Assert.Equal(45, Structured(distance).GetProperty("data").GetProperty("azimuth_degrees").GetDouble());

        var point = await surface.CallToolAsync("survey_point_by_azimuth", new Dictionary<string, JsonElement>
        {
            ["start_x"] = JsonSerializer.SerializeToElement(1000),
            ["start_y"] = JsonSerializer.SerializeToElement(2000),
            ["azimuth_degrees"] = JsonSerializer.SerializeToElement(90),
            ["distance"] = JsonSerializer.SerializeToElement(25)
        });
        AssertMcpSuccess(point);
        Assert.Equal(1025, Structured(point).GetProperty("data").GetProperty("x").GetDouble());
    }

    [Fact]
    public async Task McpAdapterCallsSurveyFbkDryRunWithoutDocumentTarget()
    {
        using var temp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-survey-fbk");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId);
        const string fbk = """
            NEZ 1 2000 1000 50 "CP-1"
            STN 1 1.5 "Setup"
            BS 2 0
            F1 VA 10 45 25 88.5 "TREE"
            """;

        var result = await surface.CallToolAsync("survey_fbk_dry_run", new Dictionary<string, JsonElement>
        {
            ["source_name"] = JsonSerializer.SerializeToElement("inline.fbk"),
            ["content"] = JsonSerializer.SerializeToElement(fbk)
        });

        AssertMcpSuccess(result);
        var data = Structured(result).GetProperty("data");
        Assert.True(data.GetProperty("dry_run").GetBoolean());
        Assert.Equal(4, data.GetProperty("record_count").GetInt32());
        Assert.Equal(1, data.GetProperty("control_point_count").GetInt32());
        Assert.Equal(1, data.GetProperty("setup_count").GetInt32());
        Assert.Equal(1, data.GetProperty("observation_count").GetInt32());
    }

    [Fact]
    public async Task McpAdapterPreservesCorrelationIdThroughPipeAndAuditLog()
    {
        using var temp = TempDirectory.Create();
        using var logTemp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-correlation");
        var surface = CreateMcpSurface(temp.Path, bridge.Descriptor.InstanceId, logTemp.Path);
        var arguments = new Dictionary<string, JsonElement>
        {
            ["correlation_id"] = JsonSerializer.SerializeToElement("corr-from-mcp")
        };

        var result = await surface.CallToolAsync("civil3d_ping", arguments);

        AssertMcpSuccess(result);
        Assert.Equal("corr-from-mcp", Structured(result).GetProperty("correlationId").GetString());
        Assert.True(bridge.Requests.TryDequeue(out var bridgeRequest));
        Assert.Equal("corr-from-mcp", bridgeRequest.CorrelationId);

        var logFile = Assert.Single(Directory.EnumerateFiles(logTemp.Path, "*.jsonl"));
        var logText = await File.ReadAllTextAsync(logFile);
        Assert.Contains("corr-from-mcp", logText);
        Assert.Contains("civil3d_ping", logText);
    }

    [Fact]
    public async Task OfficialMcpClientInitializesListsAndCallsInitialToolsOverStdio()
    {
        using var temp = TempDirectory.Create();
        using var logTemp = TempDirectory.Create();
        await using var bridge = FakeBridgeHost.Start(temp.Path, "instance-stdio");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using var client = await CreateOfficialMcpClientAsync(temp.Path, logTemp.Path, cts.Token);

        Assert.Equal(ProtocolConstants.ServerName, client.ServerInfo.Name);
        var listed = await client.ListToolsAsync(new McpListToolsRequestParams(), cts.Token);
        Assert.Equal(49, listed.Tools.Count);
        Assert.Contains(listed.Tools, tool => tool.Name == "civil3d_ping");

        foreach (var tool in new[] { "civil3d_ping", "civil3d_status", "document_list" })
        {
            var result = await client.CallToolAsync(new McpCallToolRequestParams
            {
                Name = tool
            }, cts.Token);
            AssertMcpSuccess(result);
            Assert.Equal(tool, Structured(result).GetProperty("tool").GetString());
        }

        var layerResult = await client.CallToolAsync(new McpCallToolRequestParams
        {
            Name = "layer_list",
            Arguments = LayerArgs(bridge)
        }, cts.Token);
        AssertMcpSuccess(layerResult);

        var alignmentResult = await client.CallToolAsync(new McpCallToolRequestParams
        {
            Name = "alignment_list",
            Arguments = CivilArgs(bridge)
        }, cts.Token);
        AssertMcpSuccess(alignmentResult);

        await AssertOfficialClientFbkDryRunAsync(client, cts.Token);
        await AssertOfficialClientSurveyDatabaseStatusAsync(client, cts.Token);
        await AssertOfficialClientSurveyWorkingFolderGetAsync(client, cts.Token, expectSuccess: true);
        await AssertOfficialClientSurveyProjectListAsync(client, cts.Token, expectSuccess: true);
        await AssertOfficialClientSurveyNetworkListAsync(client, cts.Token, expectSuccess: true);
    }

    [Fact]
    public async Task OfficialMcpClientStartsWithoutBridgeAndReturnsStructuredUnavailableError()
    {
        using var temp = TempDirectory.Create();
        using var logTemp = TempDirectory.Create();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using var client = await CreateOfficialMcpClientAsync(temp.Path, logTemp.Path, cts.Token);

        var listed = await client.ListToolsAsync(new McpListToolsRequestParams(), cts.Token);
        var result = await client.CallToolAsync(new McpCallToolRequestParams
        {
            Name = "civil3d_ping"
        }, cts.Token);

        Assert.Empty(listed.Tools);
        AssertMcpError(result, "BRIDGE_UNAVAILABLE");
    }

    [Fact]
    public async Task OfficialMcpClientCanCallLiveBridgeWhenConfigured()
    {
        var instanceId = Environment.GetEnvironmentVariable("CIVIL3D_HYBRID_LIVE_INSTANCE_ID");
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var endpointDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProtocolConstants.EndpointFolderName,
            ProtocolConstants.EndpointSubfolderName);
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProtocolConstants.EndpointFolderName,
            ProtocolConstants.LogSubfolderName);

        await using var client = await CreateOfficialMcpClientAsync(
            endpointDirectory,
            logDirectory,
            cts.Token,
            instanceId);

        var listed = await client.ListToolsAsync(new McpListToolsRequestParams(), cts.Token);
        Assert.Equal(49, listed.Tools.Count);
        Assert.Contains(listed.Tools, tool => tool.Name == "layer_list");
        Assert.Contains(listed.Tools, tool => tool.Name == "layer_get");
        Assert.Contains(listed.Tools, tool => tool.Name == "layer_create");
        Assert.Contains(listed.Tools, tool => tool.Name == "layer_update");
        Assert.Contains(listed.Tools, tool => tool.Name == "layer_set_current");
        Assert.Contains(listed.Tools, tool => tool.Name == "alignment_list");
        Assert.Contains(listed.Tools, tool => tool.Name == "surface_list");
        Assert.Contains(listed.Tools, tool => tool.Name == "cogo_list");
        Assert.Contains(listed.Tools, tool => tool.Name == "survey_bearing_to_azimuth");
        Assert.Contains(listed.Tools, tool => tool.Name == "survey_distance");
        Assert.Contains(listed.Tools, tool => tool.Name == "survey_fbk_dry_run");
        Assert.Contains(listed.Tools, tool => tool.Name == "survey_database_status");
        Assert.Contains(listed.Tools, tool => tool.Name == "survey_working_folder_get");
        Assert.Contains(listed.Tools, tool => tool.Name == "survey_project_list");
        Assert.Contains(listed.Tools, tool => tool.Name == "survey_network_list");

        foreach (var tool in new[] { "civil3d_ping", "civil3d_status", "document_list" })
        {
            var result = await client.CallToolAsync(new McpCallToolRequestParams
            {
                Name = tool
            }, cts.Token);

            AssertMcpSuccess(result);
            Assert.Equal(tool, Structured(result).GetProperty("tool").GetString());
        }

        await AssertOfficialClientFbkDryRunAsync(client, cts.Token);
        await AssertOfficialClientSurveyDatabaseStatusAsync(client, cts.Token);
        await AssertOfficialClientSurveyWorkingFolderGetAsync(client, cts.Token, expectSuccess: false);
        await AssertOfficialClientSurveyProjectListAsync(client, cts.Token, expectSuccess: false);
        await AssertOfficialClientSurveyNetworkListAsync(client, cts.Token, expectSuccess: false);

        var documents = await client.CallToolAsync(new McpCallToolRequestParams
        {
            Name = "document_list"
        }, cts.Token);
        AssertMcpSuccess(documents);
        var documentArray = Structured(documents).GetProperty("data").GetProperty("documents");
        if (documentArray.GetArrayLength() == 0)
        {
            return;
        }

        var drawingInfo = await client.CallToolAsync(new McpCallToolRequestParams
        {
            Name = "drawing_info"
        }, cts.Token);
        AssertMcpSuccess(drawingInfo);

        var document = documentArray[0];
        var documentId = document.GetProperty("documentId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(documentId));

        var layerArgs = new Dictionary<string, JsonElement>
        {
            ["instance_id"] = JsonSerializer.SerializeToElement(instanceId),
            ["document_id"] = JsonSerializer.SerializeToElement(documentId)
        };
        var layerList = await client.CallToolAsync(new McpCallToolRequestParams
        {
            Name = "layer_list",
            Arguments = layerArgs
        }, cts.Token);
        AssertMcpSuccess(layerList);
        var layers = Structured(layerList).GetProperty("data").GetProperty("layers");
        Assert.Contains(layers.EnumerateArray(), layer => layer.GetProperty("name").GetString() == "0");
        Assert.Contains(layers.EnumerateArray(), layer => layer.GetProperty("is_current").GetBoolean());

        var layerGetArgs = new Dictionary<string, JsonElement>(layerArgs)
        {
            ["name"] = JsonSerializer.SerializeToElement("0")
        };
        var layerGet = await client.CallToolAsync(new McpCallToolRequestParams
        {
            Name = "layer_get",
            Arguments = layerGetArgs
        }, cts.Token);
        AssertMcpSuccess(layerGet);
        Assert.Equal("0", Structured(layerGet).GetProperty("data").GetProperty("layer").GetProperty("name").GetString());
    }

    private static HybridMcpServer CreateServer(string endpointDirectory) =>
        CreateServer(new EndpointRegistryOptions
        {
            DirectoryPath = endpointDirectory,
            StaleAfter = TimeSpan.FromSeconds(60)
        });

    private static HybridMcpServer CreateServer(EndpointRegistryOptions options) =>
        new(new HybridMcpServerOptions
        {
            EndpointRegistry = options,
            RequestTimeout = TimeSpan.FromSeconds(5),
            SessionId = Guid.NewGuid().ToString("N")
        }, logger: new JsonLinesRequestLogger(Path.Combine(options.DirectoryPath, "logs")));

    private static McpToolSurface CreateMcpSurface(
        string endpointDirectory,
        string? instanceId = null,
        string? logDirectory = null) =>
        new(
            new HybridMcpServer(new HybridMcpServerOptions
            {
                EndpointRegistry = new EndpointRegistryOptions
                {
                    DirectoryPath = endpointDirectory,
                    StaleAfter = TimeSpan.FromSeconds(60)
                },
                RequestTimeout = TimeSpan.FromSeconds(5),
                SessionId = Guid.NewGuid().ToString("N")
            }, logger: new JsonLinesRequestLogger(logDirectory ?? Path.Combine(endpointDirectory, "logs"))),
            new McpToolSurfaceOptions
            {
                EndpointSelector = string.IsNullOrWhiteSpace(instanceId)
                    ? null
                    : new EndpointSelectorCriteria { InstanceId = instanceId }
            });

    private static async Task<McpClient> CreateOfficialMcpClientAsync(
        string endpointDirectory,
        string logDirectory,
        CancellationToken cancellationToken,
        string? instanceId = null)
    {
        var serverPath = Path.Combine(AppContext.BaseDirectory, "Civil3D.HybridMcp.Server.exe");
        var environmentVariables = new Dictionary<string, string?>
        {
            ["CIVIL3D_HYBRID_ENDPOINT_DIR"] = endpointDirectory,
            ["CIVIL3D_HYBRID_LOG_DIR"] = logDirectory
        };

        if (!string.IsNullOrWhiteSpace(instanceId))
        {
            environmentVariables["CIVIL3D_HYBRID_INSTANCE_ID"] = instanceId;
        }

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "civil3d-hybrid-mcp-test",
            Command = serverPath,
            Arguments = ["mcp"],
            EnvironmentVariables = environmentVariables
        });

        return await McpClient.CreateAsync(
            transport,
            new McpClientOptions
            {
                ClientInfo = new McpImplementation
                {
                    Name = "Civil3D.HybridMcp.Tests",
                    Version = "0.1.0"
                },
                ProtocolVersion = "2025-06-18",
                InitializationTimeout = TimeSpan.FromSeconds(15)
            },
            cancellationToken: cancellationToken);
    }

    private static JsonElement Structured(McpCallToolResult result)
    {
        Assert.True(result.StructuredContent.HasValue);
        return result.StructuredContent.Value;
    }

    private static async Task AssertOfficialClientFbkDryRunAsync(McpClient client, CancellationToken cancellationToken)
    {
        const string fbk = """
            NEZ 1 2000 1000 50 "CP-1"
            STN 1 1.5 "Setup"
            BS 2 0
            F1 VA 10 45 25 88.5 "TREE"
            """;

        var result = await client.CallToolAsync(new McpCallToolRequestParams
        {
            Name = "survey_fbk_dry_run",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["source_name"] = JsonSerializer.SerializeToElement("inline-smoke.fbk"),
                ["content"] = JsonSerializer.SerializeToElement(fbk)
            }
        }, cancellationToken);

        AssertMcpSuccess(result);
        var data = Structured(result).GetProperty("data");
        Assert.True(data.GetProperty("dry_run").GetBoolean());
        Assert.Equal(4, data.GetProperty("record_count").GetInt32());
        Assert.Equal(1, data.GetProperty("control_point_count").GetInt32());
        Assert.Equal(1, data.GetProperty("setup_count").GetInt32());
        Assert.Equal(1, data.GetProperty("observation_count").GetInt32());
    }

    private static async Task AssertOfficialClientSurveyDatabaseStatusAsync(McpClient client, CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync(new McpCallToolRequestParams
        {
            Name = "survey_database_status"
        }, cancellationToken);

        AssertMcpSuccess(result);
        var data = Structured(result).GetProperty("data");
        var adapter = data.GetProperty("adapter").GetString();
        var status = data.GetProperty("status").GetString();
        Assert.Contains(adapter, new[] { "none", "environment_probe", "fake" });
        Assert.Contains(status, new[] { "not_configured", "interop_not_found", "interop_incomplete", "interop_available" });
        if (string.Equals(adapter, "none", StringComparison.Ordinal))
        {
            Assert.Equal("not_configured", status);
            Assert.False(data.GetProperty("survey_com_available").GetBoolean());
        }

        Assert.False(data.GetProperty("survey_database_available").GetBoolean());
        Assert.True(data.GetProperty("requires_active_document").GetBoolean());
    }

    private static async Task AssertOfficialClientSurveyWorkingFolderGetAsync(
        McpClient client,
        CancellationToken cancellationToken,
        bool expectSuccess)
    {
        var result = await client.CallToolAsync(new McpCallToolRequestParams
        {
            Name = "survey_working_folder_get"
        }, cancellationToken);

        if (!expectSuccess)
        {
            AssertMcpError(result, "DOCUMENT_NOT_FOUND");
            Assert.Equal("DocumentNotFound", Structured(result).GetProperty("error").GetProperty("internalCode").GetString());
            return;
        }

        AssertMcpSuccess(result);
        var data = Structured(result).GetProperty("data");
        Assert.Equal("working_folder_available", data.GetProperty("status").GetString());
        Assert.Equal(@"C:\SurveyData", data.GetProperty("working_folder").GetString());
        Assert.True(data.GetProperty("requires_active_document").GetBoolean());
    }

    private static async Task AssertOfficialClientSurveyProjectListAsync(
        McpClient client,
        CancellationToken cancellationToken,
        bool expectSuccess)
    {
        var result = await client.CallToolAsync(new McpCallToolRequestParams
        {
            Name = "survey_project_list"
        }, cancellationToken);

        if (!expectSuccess)
        {
            AssertMcpError(result, "DOCUMENT_NOT_FOUND");
            Assert.Equal("DocumentNotFound", Structured(result).GetProperty("error").GetProperty("internalCode").GetString());
            return;
        }

        AssertMcpSuccess(result);
        var data = Structured(result).GetProperty("data");
        Assert.Equal("projects_available", data.GetProperty("status").GetString());
        Assert.Equal(@"C:\SurveyData", data.GetProperty("working_folder").GetString());
        Assert.Equal(2, data.GetProperty("count").GetInt32());
        Assert.Equal("Topo-2026", data.GetProperty("projects")[0].GetProperty("name").GetString());
        Assert.Equal(@"{11111111-1111-1111-1111-111111111111}", data.GetProperty("projects")[0].GetProperty("project_guid").GetString());
        Assert.True(data.GetProperty("requires_active_document").GetBoolean());
    }

    private static async Task AssertOfficialClientSurveyNetworkListAsync(
        McpClient client,
        CancellationToken cancellationToken,
        bool expectSuccess)
    {
        var result = await client.CallToolAsync(new McpCallToolRequestParams
        {
            Name = "survey_network_list"
        }, cancellationToken);

        if (!expectSuccess)
        {
            AssertMcpError(result, "DOCUMENT_NOT_FOUND");
            Assert.Equal("DocumentNotFound", Structured(result).GetProperty("error").GetProperty("internalCode").GetString());
            return;
        }

        AssertMcpSuccess(result);
        var data = Structured(result).GetProperty("data");
        Assert.Equal("networks_available", data.GetProperty("status").GetString());
        Assert.Equal(@"C:\SurveyData", data.GetProperty("working_folder").GetString());
        Assert.Equal(3, data.GetProperty("count").GetInt32());
        Assert.Equal("NET-CONTROLE", data.GetProperty("networks")[0].GetProperty("name").GetString());
        Assert.Equal("Topo-2026", data.GetProperty("networks")[0].GetProperty("project_name").GetString());
        Assert.True(data.GetProperty("requires_active_document").GetBoolean());

        var filtered = await client.CallToolAsync(new McpCallToolRequestParams
        {
            Name = "survey_network_list",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["project_name"] = JsonSerializer.SerializeToElement("Control")
            }
        }, cancellationToken);

        AssertMcpSuccess(filtered);
        var filteredData = Structured(filtered).GetProperty("data");
        Assert.Equal("Control", filteredData.GetProperty("project_name").GetString());
        Assert.Equal(1, filteredData.GetProperty("count").GetInt32());
        Assert.Equal("NET-MARCOS", filteredData.GetProperty("networks")[0].GetProperty("name").GetString());
    }

    private static void AssertMcpSuccess(McpCallToolResult result)
    {
        Assert.False(result.IsError ?? false);
        Assert.True(Structured(result).GetProperty("success").GetBoolean());
    }

    private static void AssertMcpError(McpCallToolResult result, string expectedCode)
    {
        Assert.True(result.IsError ?? false);
        var structured = Structured(result);
        Assert.False(structured.GetProperty("success").GetBoolean());
        Assert.Equal(expectedCode, structured.GetProperty("error").GetProperty("code").GetString());
    }

    private static EndpointDescriptor bridgeDescriptor(string instanceId)
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return new EndpointDescriptor
        {
            InstanceId = instanceId,
            ProcessId = process.Id,
            ProcessStartTimeUtc = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero),
            Product = "Autodesk Civil 3D",
            ProductVersion = "2026",
            BridgeVersion = ProtocolConstants.BridgeVersion,
            ProtocolVersion = ProtocolVersion.Current,
            PipeName = $"{ProtocolConstants.PipePrefix}-{process.Id}-{instanceId}",
            StartedUtc = DateTimeOffset.UtcNow,
            LastHeartbeatUtc = DateTimeOffset.UtcNow,
            Capabilities = new CapabilityManifest
            {
                InstanceId = instanceId,
                Product = "Autodesk Civil 3D",
                ProductVersion = "2026",
                Capabilities = new CapabilityFlags
                {
                    Core = true,
                    AutoCad = true,
                    Civil3D = true,
                    Diagnostics = true,
                    DialogMonitor = "partial"
                }
            }
        };
    }

    private static Dictionary<string, JsonElement> LayerArgs(FakeBridgeHost bridge) =>
        new()
        {
            ["instance_id"] = JsonSerializer.SerializeToElement(bridge.Descriptor.InstanceId),
            ["document_id"] = JsonSerializer.SerializeToElement(bridge.Descriptor.ActiveDocument!.DocumentId)
        };

    private static Dictionary<string, JsonElement> CivilArgs(FakeBridgeHost bridge) =>
        new()
        {
            ["instance_id"] = JsonSerializer.SerializeToElement(bridge.Descriptor.InstanceId),
            ["document_id"] = JsonSerializer.SerializeToElement(bridge.Descriptor.ActiveDocument!.DocumentId)
        };
}

internal sealed class TempDirectory : IDisposable
{
    private TempDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TempDirectory Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Civil3DHybridMcpIntegration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
