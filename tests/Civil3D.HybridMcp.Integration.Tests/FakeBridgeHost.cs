using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;
using Civil3D.HybridMcp.Sdk;
using Civil3D.HybridMcp.Tools.Survey;

namespace Civil3D.HybridMcp.Integration.Tests;

internal sealed class FakeBridgeHost : IAsyncDisposable
{
    private readonly EndpointRegistryOptions _options;
    private readonly EndpointRegistryWriter _writer;
    private readonly NamedPipeRpcServer _server;
    private readonly ProtocolVersion _protocolVersion;
    private readonly IReadOnlyList<ToolDescriptor> _tools;
    private readonly IReadOnlyDictionary<string, ErrorEnvelope> _forcedErrors;
    private readonly int _delayMs;
    private readonly List<LayerInfo> _layers;
    private readonly List<AlignmentInfo> _alignments;
    private readonly List<SurfaceInfo> _surfaces;
    private readonly List<CogoPointInfo> _cogoPoints;

    private readonly List<ProfileInfo> _profiles =
    [
        new()
        {
            Name = "EG-MAIN",
            Handle = "P1",
            AlignmentName = "MAIN_ROAD",
            AlignmentHandle = "A1",
            ProfileType = "EG",
            StartingStation = 0,
            EndingStation = 500,
            Length = 500,
            Offset = 0,
            ElevationMin = 100.5,
            ElevationMax = 130.25,
            PviCount = 0,
            StyleName = "Existing Ground"
        },
        new()
        {
            Name = "FG-MAIN",
            Handle = "P2",
            AlignmentName = "MAIN_ROAD",
            AlignmentHandle = "A1",
            ProfileType = "FG",
            StartingStation = 0,
            EndingStation = 500,
            Length = 500,
            Offset = 0,
            ElevationMin = 101,
            ElevationMax = 129,
            PviCount = 6,
            StyleName = "Design Profile"
        }
    ];

    private readonly List<CorridorInfo> _corridors =
    [
        new()
        {
            Name = "CORR-BD-100",
            Handle = "K1",
            IsOutOfDate = false,
            RebuildAutomatic = true,
            CodeSetStyleName = "_MP Codes",
            MaximumTriangleSideLength = 30.5,
            RegionLockMode = "Unlocked",
            BaselineCount = 1,
            CorridorSurfaceCount = 1,
            Baselines =
            [
                new CorridorBaselineInfo
                {
                    Name = "BL - BD - 100",
                    BaselineType = "AlignmentProfile",
                    AlignmentName = "MAIN_ROAD",
                    AlignmentHandle = "A1",
                    ProfileName = "FG-MAIN",
                    ProfileHandle = "P2",
                    StartStation = 0,
                    EndStation = 500,
                    RegionCount = 1,
                    Regions =
                    [
                        new CorridorRegionInfo
                        {
                            Name = "RG - BD - 100 - 1",
                            StartStation = 0,
                            EndStation = 500,
                            AssemblyName = "ASSEMBLY-PISTA",
                            AssemblyHandle = "S1"
                        }
                    ]
                }
            ]
        }
    ];

    private readonly List<PropertySetDefinitionInfo> _propertySetDefinitions =
    [
        new()
        {
            Name = "Rodovia",
            Handle = "PSD1",
            IsStyleBased = false,
            IsVisible = true,
            IsWriteable = true,
            AppliesToAll = false,
            AppliesTo = ["AeccDbAlignment"],
            PropertyCount = 2,
            Properties =
            [
                new PropertyDefinitionInfo { Name = "Codigo", DataType = "Text", IsReadOnly = false },
                new PropertyDefinitionInfo { Name = "Extensao", DataType = "Real", Units = "Distance", IsReadOnly = false }
            ]
        }
    ];

    private readonly List<PropertySetInfo> _propertySets =
    [
        new()
        {
            Name = "Rodovia",
            Handle = "PS1",
            DefinitionHandle = "PSD1",
            PropertyCount = 2,
            Properties =
            [
                new PropertyValueInfo { Name = "Codigo", Id = 1, DataType = "Text", Value = "BR-040" },
                new PropertyValueInfo { Name = "Extensao", Id = 2, DataType = "Real", Value = "12.5", NumericValue = 12.5, Units = "Distance" }
            ]
        }
    ];

    private readonly Dictionary<string, List<PropertySetInfo>> _createdPropertySetsByObject = new(StringComparer.OrdinalIgnoreCase);

    private FakeBridgeHost(
        EndpointRegistryOptions options,
        EndpointDescriptor descriptor,
        NamedPipeRpcServer server,
        ProtocolVersion protocolVersion,
        IReadOnlyList<ToolDescriptor> tools,
        IReadOnlyDictionary<string, ErrorEnvelope> forcedErrors,
        int delayMs,
        List<LayerInfo> layers,
        List<AlignmentInfo> alignments,
        List<SurfaceInfo> surfaces,
        List<CogoPointInfo> cogoPoints)
    {
        _options = options;
        _writer = new EndpointRegistryWriter(options);
        Descriptor = descriptor;
        _server = server;
        _protocolVersion = protocolVersion;
        _tools = tools;
        _forcedErrors = forcedErrors;
        _delayMs = delayMs;
        _layers = layers;
        _alignments = alignments;
        _surfaces = surfaces;
        _cogoPoints = cogoPoints;
    }

    public EndpointDescriptor Descriptor { get; }
    public ConcurrentQueue<ToolRequest> Requests { get; } = new();

    public static FakeBridgeHost Start(
        string endpointDirectory,
        string? instanceId = null,
        ProtocolVersion? protocolVersion = null,
        int delayMs = 0,
        TimeSpan? toolTimeout = null,
        IReadOnlyDictionary<string, ErrorEnvelope>? forcedErrors = null)
    {
        var options = new EndpointRegistryOptions
        {
            DirectoryPath = endpointDirectory,
            StaleAfter = TimeSpan.FromSeconds(60)
        };
        var version = protocolVersion ?? ProtocolVersion.Current;
        var id = instanceId ?? Guid.NewGuid().ToString("N");
        var process = Process.GetCurrentProcess();
        var pipeName = $"{ProtocolConstants.PipePrefix}-{process.Id}-{id}";
        var tools = CreateTools(toolTimeout ?? TimeSpan.FromSeconds(15));
        var document = CreateDocument(id);
        var layers = CreateLayers(id, document.DocumentId).ToList();
        var alignments = CreateAlignments(id, document.DocumentId).ToList();
        var surfaces = CreateSurfaces(id, document.DocumentId).ToList();
        var cogoPoints = CreateCogoPoints(id, document.DocumentId).ToList();
        var capabilities = CreateCapabilities(id, version, tools);
        var descriptor = new EndpointDescriptor
        {
            InstanceId = id,
            ProcessId = process.Id,
            ProcessStartTimeUtc = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero),
            Product = "Autodesk Civil 3D",
            ProductVersion = "2026",
            BridgeVersion = ProtocolConstants.BridgeVersion,
            ProtocolVersion = version,
            PipeName = pipeName,
            StartedUtc = DateTimeOffset.UtcNow,
            LastHeartbeatUtc = DateTimeOffset.UtcNow,
            ActiveDocument = document,
            Capabilities = capabilities
        };

        FakeBridgeHost? host = null;
        var server = new NamedPipeRpcServer(pipeName, async (request, ct) =>
        {
            if (host is null)
            {
                return JsonRpcResponse.Failure(request.Id, ErrorCode.BridgeUnavailable, "Fake bridge not initialized.");
            }

            return await host.HandleAsync(request, ct);
        });

        host = new FakeBridgeHost(
            options,
            descriptor,
            server,
            version,
            tools,
            forcedErrors ?? new Dictionary<string, ErrorEnvelope>(StringComparer.OrdinalIgnoreCase),
            delayMs,
            layers,
            alignments,
            surfaces,
            cogoPoints);
        server.Start();
        host._writer.Write(descriptor);
        return host;
    }

    public async ValueTask DisposeAsync()
    {
        _writer.Remove(Descriptor);
        await _server.DisposeAsync();
    }

    private async Task<JsonRpcResponse> HandleAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        switch (request.Method)
        {
            case "bridge/handshake":
                var handshake = request.Params!.Deserialize<HandshakeRequest>(JsonContractOptions.Default)!;
                var compatibility = VersionNegotiator.Evaluate(handshake.ProtocolVersion, _protocolVersion);
                return JsonRpcResponse.Success(request.Id, new HandshakeResponse
                {
                    Success = compatibility.Status != CompatibilityStatus.Incompatible,
                    Compatibility = compatibility.Status,
                    Message = compatibility.Reason,
                    Instance = new InstanceDescriptor
                    {
                        InstanceId = Descriptor.InstanceId,
                        ProcessId = Descriptor.ProcessId,
                        ProcessStartTimeUtc = Descriptor.ProcessStartTimeUtc,
                        Product = Descriptor.Product,
                        ProductVersion = Descriptor.ProductVersion,
                        BridgeVersion = Descriptor.BridgeVersion,
                        ProtocolVersion = Descriptor.ProtocolVersion,
                        PipeName = Descriptor.PipeName,
                        ActiveDocument = Descriptor.ActiveDocument,
                        DocumentCount = 1
                    },
                    CapabilityManifest = Descriptor.Capabilities,
                    Tools = _tools,
                    Error = compatibility.Status == CompatibilityStatus.Incompatible
                        ? new ErrorEnvelope { Code = ErrorCode.ProtocolIncompatible, Message = compatibility.Reason }
                        : null
                });

            case "tools/list":
                return JsonRpcResponse.Success(request.Id, new ToolManifest
                {
                    Version = ProtocolVersion.Current,
                    Tools = _tools
                });

            case "tools/execute":
                var toolRequest = request.Params!.Deserialize<ToolRequest>(JsonContractOptions.Default)!;
                if (_delayMs > 0)
                {
                    await Task.Delay(_delayMs, cancellationToken);
                }

                return JsonRpcResponse.Success(request.Id, ExecuteTool(toolRequest));

            default:
                return JsonRpcResponse.Failure(request.Id, ErrorCode.InvalidRequest, $"Unknown method '{request.Method}'.");
        }
    }

    private ToolResponse ExecuteTool(ToolRequest request)
    {
        Requests.Enqueue(request);

        if (_forcedErrors.TryGetValue(request.ToolName, out var error))
        {
            return new ToolResponse
            {
                Success = false,
                Tool = request.ToolName,
                CorrelationId = request.CorrelationId,
                Document = Descriptor.ActiveDocument,
                Error = error with
                {
                    CorrelationId = error.CorrelationId ?? request.CorrelationId
                }
            };
        }

        return request.ToolName switch
        {
            "layer_list" => ExecuteLayerList(request),
            "layer_get" => ExecuteLayerGet(request),
            "object_get" => ExecuteObjectGet(request),
            "object_properties" => ExecuteObjectProperties(request),
            "block_list" => ExecuteBlockList(request),
            "block_reference_list" => ExecuteBlockReferenceList(request),
            "block_get_attributes" => ExecuteBlockGetAttributes(request),
            "layer_create" => ExecuteLayerCreate(request),
            "layer_update" => ExecuteLayerUpdate(request),
            "layer_set_current" => ExecuteLayerSetCurrent(request),
            "alignment_list" => ExecuteAlignmentList(request),
            "alignment_get" => ExecuteAlignmentGet(request),
            "alignment_station_offset" => ExecuteAlignmentStationOffset(request),
            "alignment_point_location" => ExecuteAlignmentPointLocation(request),
            "surface_list" => ExecuteSurfaceList(request),
            "surface_get" => ExecuteSurfaceGet(request),
            "surface_elevation_at_xy" => ExecuteSurfaceElevationAtXy(request),
            "cogo_list" => ExecuteCogoList(request),
            "cogo_get" => ExecuteCogoGet(request),
            "profile_list" => ExecuteProfileList(request),
            "profile_get" => ExecuteProfileGet(request),
            "profile_elevation_at_station" => ExecuteProfileElevationAtStation(request),
            "corridor_list" => ExecuteCorridorList(request),
            "corridor_get" => ExecuteCorridorGet(request),
            "propertyset_definition_list" => ExecutePropertySetDefinitionList(request),
            "propertyset_list" => ExecutePropertySetList(request),
            "propertyset_get" => ExecutePropertySetGet(request),
            "propertyset_numeric_test_create" => ExecutePropertySetNumericTestCreate(request),
            "survey_bearing_to_azimuth" => ExecuteSurveyTool(request),
            "survey_azimuth_to_bearing" => ExecuteSurveyTool(request),
            "survey_distance" => ExecuteSurveyTool(request),
            "survey_point_by_azimuth" => ExecuteSurveyTool(request),
            "survey_fbk_dry_run" => ExecuteSurveyTool(request),
            "survey_database_status" => ExecuteSurveyTool(request),
            "survey_working_folder_get" => ExecuteSurveyTool(request),
            "survey_project_list" => ExecuteSurveyTool(request),
            "survey_network_list" => ExecuteSurveyTool(request),
            _ => Success(request, request.ToolName switch
            {
                "civil3d_ping" => new PingResponse
                {
                    InstanceId = Descriptor.InstanceId,
                    BridgeVersion = Descriptor.BridgeVersion,
                    ProtocolVersion = Descriptor.ProtocolVersion,
                    ProductVersion = Descriptor.ProductVersion
                },
                "civil3d_status" => new StatusResponse
                {
                    Civil3DRunning = true,
                    Instance = new InstanceDescriptor
                    {
                        InstanceId = Descriptor.InstanceId,
                        ProcessId = Descriptor.ProcessId,
                        ProcessStartTimeUtc = Descriptor.ProcessStartTimeUtc,
                        Product = Descriptor.Product,
                        ProductVersion = Descriptor.ProductVersion,
                        BridgeVersion = Descriptor.BridgeVersion,
                        ProtocolVersion = Descriptor.ProtocolVersion,
                        PipeName = Descriptor.PipeName,
                        ActiveDocument = Descriptor.ActiveDocument,
                        DocumentCount = 1
                    },
                    DocumentCount = 1,
                    ActiveDocument = Descriptor.ActiveDocument,
                    DialogState = new DialogStatus
                    {
                        Supported = true,
                        State = "active",
                        HasBlockingDialog = false,
                        DialogCount = 0,
                        Note = "Fake bridge reports no blocking dialog."
                    },
                    Capabilities = Descriptor.Capabilities,
                    BridgeHealth = new BridgeHealth
                    {
                        PipeListening = true,
                        EndpointRegistered = true
                    }
                },
                "document_list" => new DocumentListResponse
                {
                    InstanceId = Descriptor.InstanceId,
                    Documents = new[] { Descriptor.ActiveDocument! }
                },
                "drawing_info" => new DrawingInfoResponse
                {
                    Document = Descriptor.ActiveDocument!
                },
                "list_dialogs" => new DialogListResponse
                {
                    InstanceId = Descriptor.InstanceId,
                    ProcessId = Descriptor.ProcessId,
                    MainWindowHandle = "0x0000000000000100",
                    MainWindowTitle = "Autodesk Civil 3D 2026",
                    MainWindowEnabled = true,
                    BlockedByModal = false,
                    Count = 0,
                    Capabilities = ["modal_detection", "host_window_enumeration"],
                    Limitations = ["Fake bridge reports a synthetic window tree."]
                },
                _ => throw new InvalidOperationException($"Unknown fake tool '{request.ToolName}'.")
            })
        };
    }

    private ToolResponse ExecuteSurveyTool(ToolRequest request)
    {
        try
        {
            var service = new SurveyGeometryService(new FakeSurveyDatabaseGateway());
            object data = request.ToolName.ToLowerInvariant() switch
            {
                "survey_bearing_to_azimuth" => service.BearingToAzimuth(Descriptor.InstanceId, ReadSurveyArguments<SurveyBearingToAzimuthRequest>(request)),
                "survey_azimuth_to_bearing" => service.AzimuthToBearing(Descriptor.InstanceId, ReadSurveyArguments<SurveyAzimuthToBearingRequest>(request)),
                "survey_distance" => service.Distance(Descriptor.InstanceId, ReadSurveyArguments<SurveyDistanceRequest>(request)),
                "survey_point_by_azimuth" => service.PointByAzimuth(Descriptor.InstanceId, ReadSurveyArguments<SurveyPointByAzimuthRequest>(request)),
                "survey_fbk_dry_run" => service.FbkDryRun(Descriptor.InstanceId, ReadSurveyArguments<SurveyFbkDryRunRequest>(request)),
                "survey_database_status" => service.DatabaseStatus(Descriptor.InstanceId, ReadOptionalSurveyArguments<SurveyDatabaseStatusRequest>(request)),
                "survey_working_folder_get" => service.WorkingFolderGet(Descriptor.InstanceId, ReadOptionalSurveyArguments<SurveyWorkingFolderGetRequest>(request)),
                "survey_project_list" => service.ProjectList(Descriptor.InstanceId, ReadOptionalSurveyArguments<SurveyProjectListRequest>(request)),
                "survey_network_list" => service.NetworkList(Descriptor.InstanceId, ReadOptionalSurveyArguments<SurveyNetworkListRequest>(request)),
                _ => throw new SurveyToolException(ErrorCode.ToolNotFound, $"Unknown fake Survey tool '{request.ToolName}'.")
            };

            return Success(request, data);
        }
        catch (SurveyToolException ex)
        {
            return Error(request, ex.Code, ex.Message);
        }
        catch (JsonException ex)
        {
            return Error(request, ErrorCode.InvalidRequest, ex.Message);
        }
    }

    private ToolResponse ExecuteLayerList(ToolRequest request)
    {
        var parsed = ReadLayerArguments<LayerListRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var layers = _layers
            .Where(layer =>
                (string.IsNullOrWhiteSpace(input.NameContains) ||
                    layer.Name.Contains(input.NameContains, StringComparison.OrdinalIgnoreCase)) &&
                (input.IsOn is null || layer.IsOn == input.IsOn) &&
                (input.IsFrozen is null || layer.IsFrozen == input.IsFrozen) &&
                (input.IsLocked is null || layer.IsLocked == input.IsLocked) &&
                (input.IsPlottable is null || layer.IsPlottable == input.IsPlottable))
            .ToArray();

        return Success(request, new LayerListResponse
        {
            InstanceId = Descriptor.InstanceId,
            DocumentId = Descriptor.ActiveDocument!.DocumentId,
            Count = layers.Length,
            Layers = layers
        });
    }

    private ToolResponse ExecuteLayerGet(ToolRequest request)
    {
        var parsed = ReadLayerArguments<LayerGetRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var layer = FindLayer(input.Name, input.Handle, input.ObjectReference);

        return layer is null
            ? Error(request, ErrorCode.LayerNotFound, "Layer was not found in the target drawing.")
            : Success(request, new LayerGetResponse
            {
                InstanceId = Descriptor.InstanceId,
                DocumentId = Descriptor.ActiveDocument!.DocumentId,
                Layer = layer
            });
    }

    private ToolResponse ExecuteObjectGet(ToolRequest request)
    {
        var parsed = ReadLayerArguments<ObjectGetRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var handle = FirstNonBlank(input.Handle, input.ObjectReference?.Handle);
        if (handle is null)
        {
            return Error(request, ErrorCode.InvalidRequest, "object_get requires handle or object_reference.handle.");
        }

        return string.Equals(handle, FakeObjectHandle, StringComparison.OrdinalIgnoreCase)
            ? Success(request, new ObjectGetResponse
            {
                InstanceId = Descriptor.InstanceId,
                DocumentId = Descriptor.ActiveDocument!.DocumentId,
                Object = FakeObject()
            })
            : Error(request, ErrorCode.ObjectNotFound, $"Handle '{handle}' does not resolve to an object in the target drawing.");
    }

    /// <summary>
    /// Mirrors the shape the real block reads return, including the case the
    /// block tools exist to get right: a dynamic insert whose definition is the
    /// anonymous *U12 but whose block name is PLACA.
    /// </summary>
    private ToolResponse ExecuteBlockList(ToolRequest request)
    {
        var parsed = ReadLayerArguments<BlockListRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        return Success(request, new BlockListResponse
        {
            InstanceId = Descriptor.InstanceId,
            DocumentId = Descriptor.ActiveDocument!.DocumentId,
            Count = 1,
            Blocks =
            [
                new BlockDefinitionInfo
                {
                    Name = "PLACA",
                    Handle = "1A0",
                    IsDynamic = true,
                    HasAttributeDefinitions = true,
                    ReferenceCount = 2,
                    AttributeDefinitions = [new BlockAttributeDefinitionInfo { Tag = "TIPO", Constant = true, DefaultValue = "R-19" }]
                }
            ]
        });
    }

    private ToolResponse ExecuteBlockReferenceList(ToolRequest request)
    {
        var parsed = ReadLayerArguments<BlockReferenceListRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        return Success(request, new BlockReferenceListResponse
        {
            InstanceId = Descriptor.InstanceId,
            DocumentId = Descriptor.ActiveDocument!.DocumentId,
            Count = 1,
            ScannedCount = 4146,
            Truncated = false,
            TruncatedBy = null,
            References =
            [
                new BlockReferenceInfo
                {
                    Handle = "2B1",
                    BlockName = "PLACA",
                    DefinitionName = "*U12",
                    IsDynamic = true,
                    Layer = "F-HZ-SINALIZACAO",
                    Space = "Model",
                    X = 1000.5,
                    Y = 2000.25,
                    RotationRadians = 1.5707963267948966,
                    ScaleX = 1,
                    ScaleY = 1,
                    ScaleZ = 1,
                    AttributeCount = 1
                }
            ]
        });
    }

    private ToolResponse ExecuteBlockGetAttributes(ToolRequest request)
    {
        var parsed = ReadLayerArguments<BlockGetAttributesRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var handle = FirstNonBlank(parsed.Value!.Handle, parsed.Value.ObjectReference?.Handle);
        if (!string.Equals(handle, "2B1", StringComparison.OrdinalIgnoreCase))
        {
            return Error(request, ErrorCode.ObjectNotFound, $"Handle '{handle}' does not resolve to an object in the target drawing.");
        }

        return Success(request, new BlockGetAttributesResponse
        {
            InstanceId = Descriptor.InstanceId,
            DocumentId = Descriptor.ActiveDocument!.DocumentId,
            Handle = "2B1",
            BlockName = "PLACA",
            DefinitionName = "*U12",
            IsDynamic = true,
            Count = 2,
            Attributes =
            [
                new BlockAttributeInfo { Tag = "KM", Value = "172+700", Handle = "2B2" },
                new BlockAttributeInfo { Tag = "TIPO", Value = "R-19", Handle = null, Constant = true }
            ]
        });
    }

    private ToolResponse ExecuteObjectProperties(ToolRequest request)
    {
        var parsed = ReadLayerArguments<ObjectPropertiesRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var handle = FirstNonBlank(input.Handle, input.ObjectReference?.Handle);
        if (handle is null)
        {
            return Error(request, ErrorCode.InvalidRequest, "object_properties requires handle or object_reference.handle.");
        }

        if (!string.Equals(handle, FakeObjectHandle, StringComparison.OrdinalIgnoreCase))
        {
            return Error(request, ErrorCode.ObjectNotFound, $"Handle '{handle}' does not resolve to an object in the target drawing.");
        }

        var includeUnreadable = input.IncludeUnreadable ?? false;
        var properties = FakeObjectProperties()
            .Where(property => includeUnreadable || property.Value is not null)
            .Where(property => string.IsNullOrWhiteSpace(input.NameContains) ||
                               property.Name.Contains(input.NameContains, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return Success(request, new ObjectPropertiesResponse
        {
            InstanceId = Descriptor.InstanceId,
            DocumentId = Descriptor.ActiveDocument!.DocumentId,
            Handle = handle,
            ObjectClass = "AcDbLine",
            Count = properties.Length,
            Truncated = false,
            Properties = properties
        });
    }

    private ToolResponse ExecuteLayerCreate(ToolRequest request)
    {
        var parsed = ReadLayerArguments<LayerCreateRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return Error(request, ErrorCode.InvalidLayerName, "Layer name must not be empty.");
        }

        if (_layers.Any(layer => string.Equals(layer.Name, input.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return Error(request, ErrorCode.LayerAlreadyExists, $"Layer '{input.Name}' already exists in the target drawing.");
        }

        var handle = (0x20 + _layers.Count).ToString("X");
        var layer = CreateLayer(
            Descriptor.InstanceId,
            Descriptor.ActiveDocument!.DocumentId,
            input.Name,
            handle,
            isPlottable: input.IsPlottable ?? true) with
        {
            Description = input.Description,
            Color = input.ColorIndex is { } colorIndex
                ? new LayerColorInfo { ColorIndex = colorIndex, Method = "ByAci" }
                : new LayerColorInfo { ColorIndex = 7, Method = "ByAci" }
        };

        _layers.Add(layer);
        return Success(request, new LayerCreateResponse
        {
            InstanceId = Descriptor.InstanceId,
            DocumentId = Descriptor.ActiveDocument!.DocumentId,
            Created = true,
            Layer = layer,
            Transaction = new WriteTransactionInfo
            {
                DocumentLocked = true,
                TransactionStarted = true,
                Committed = true,
                RolledBack = false
            }
        });
    }

    private ToolResponse ExecuteLayerUpdate(ToolRequest request)
    {
        var parsed = ReadLayerArguments<LayerUpdateRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        if (string.IsNullOrWhiteSpace(input.Name) &&
            string.IsNullOrWhiteSpace(input.Handle) &&
            string.IsNullOrWhiteSpace(input.ObjectReference?.Name) &&
            string.IsNullOrWhiteSpace(input.ObjectReference?.Handle))
        {
            return Error(request, ErrorCode.InvalidRequest, "layer_update requires name, handle, or object_reference with name/handle.");
        }

        if (input.NewName is null &&
            input.ColorIndex is null &&
            input.IsOn is null &&
            input.IsFrozen is null &&
            input.IsLocked is null &&
            input.IsPlottable is null &&
            input.Description is null)
        {
            return Error(request, ErrorCode.InvalidRequest, "layer_update requires at least one update field.");
        }

        if (input.ColorIndex is < 1 or > 255)
        {
            return Error(request, ErrorCode.InvalidRequest, "color_index must be between 1 and 255 when provided.");
        }

        if (input.NewName is not null && string.IsNullOrWhiteSpace(input.NewName))
        {
            return Error(request, ErrorCode.InvalidLayerName, "Layer name must not be empty.");
        }

        var index = _layers.FindIndex(layer =>
            string.Equals(layer.Name, input.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(layer.Handle, input.Handle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(layer.Name, input.ObjectReference?.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(layer.Handle, input.ObjectReference?.Handle, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            return Error(request, ErrorCode.LayerNotFound, "Layer was not found in the target drawing.");
        }

        var previous = _layers[index];
        if (previous.IsCurrent && input.IsFrozen == true)
        {
            return Error(request, ErrorCode.InvalidRequest, "The current layer cannot be frozen.");
        }

        if (previous.IsCurrent && input.IsOn == false)
        {
            return Error(request, ErrorCode.InvalidRequest, "The current layer cannot be turned off.");
        }

        if (!string.IsNullOrWhiteSpace(input.NewName) &&
            !string.Equals(previous.Name, input.NewName, StringComparison.OrdinalIgnoreCase) &&
            _layers.Any(layer => string.Equals(layer.Name, input.NewName, StringComparison.OrdinalIgnoreCase)))
        {
            return Error(request, ErrorCode.LayerAlreadyExists, $"Layer '{input.NewName}' already exists in the target drawing.");
        }

        var updatedName = input.NewName ?? previous.Name;
        var updated = previous with
        {
            Name = updatedName,
            ObjectReference = previous.ObjectReference is null
                ? null
                : previous.ObjectReference with { Name = updatedName },
            IsOn = input.IsOn ?? previous.IsOn,
            IsFrozen = input.IsFrozen ?? previous.IsFrozen,
            IsLocked = input.IsLocked ?? previous.IsLocked,
            IsPlottable = input.IsPlottable ?? previous.IsPlottable,
            Color = input.ColorIndex is { } colorIndex
                ? new LayerColorInfo { ColorIndex = colorIndex, Method = "ByAci" }
                : previous.Color,
            Description = input.Description ?? previous.Description
        };

        _layers[index] = updated;
        return Success(request, new LayerUpdateResponse
        {
            InstanceId = Descriptor.InstanceId,
            DocumentId = Descriptor.ActiveDocument!.DocumentId,
            Changed = previous != updated,
            PreviousLayer = previous,
            UpdatedLayer = updated,
            Transaction = new WriteTransactionInfo
            {
                DocumentLocked = true,
                TransactionStarted = true,
                Committed = true,
                RolledBack = false
            }
        });
    }

    private ToolResponse ExecuteLayerSetCurrent(ToolRequest request)
    {
        var parsed = ReadLayerArguments<LayerSetCurrentRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        if (string.IsNullOrWhiteSpace(input.Name) &&
            string.IsNullOrWhiteSpace(input.Handle) &&
            string.IsNullOrWhiteSpace(input.ObjectReference?.Name) &&
            string.IsNullOrWhiteSpace(input.ObjectReference?.Handle))
        {
            return Error(request, ErrorCode.InvalidRequest, "layer_set_current requires name, handle, or object_reference with name/handle.");
        }

        var previousIndex = _layers.FindIndex(layer => layer.IsCurrent);
        var targetIndex = _layers.FindIndex(layer =>
            string.Equals(layer.Name, input.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(layer.Handle, input.Handle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(layer.Name, input.ObjectReference?.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(layer.Handle, input.ObjectReference?.Handle, StringComparison.OrdinalIgnoreCase));

        if (targetIndex < 0)
        {
            return Error(request, ErrorCode.LayerNotFound, "Layer was not found in the target drawing.");
        }

        var previousLayer = previousIndex >= 0
            ? _layers[previousIndex]
            : _layers[0];
        var changed = previousIndex != targetIndex;
        if (changed && previousIndex >= 0)
        {
            _layers[previousIndex] = _layers[previousIndex] with { IsCurrent = false };
        }

        _layers[targetIndex] = _layers[targetIndex] with { IsCurrent = true };

        return Success(request, new LayerSetCurrentResponse
        {
            InstanceId = Descriptor.InstanceId,
            DocumentId = Descriptor.ActiveDocument!.DocumentId,
            Changed = changed,
            PreviousLayer = previousLayer,
            CurrentLayer = _layers[targetIndex],
            Transaction = new WriteTransactionInfo
            {
                DocumentLocked = true,
                TransactionStarted = true,
                Committed = true,
                RolledBack = false
            }
        });
    }

    private ToolResponse ExecuteAlignmentList(ToolRequest request)
    {
        var parsed = ReadCivilArguments<CivilObjectListRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var alignments = _alignments
            .Where(item => string.IsNullOrWhiteSpace(input.NameContains) || item.Name.Contains(input.NameContains, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return Success(request, new AlignmentListResponse
        {
            InstanceId = Descriptor.InstanceId,
            DocumentId = Descriptor.ActiveDocument!.DocumentId,
            Count = alignments.Length,
            Alignments = alignments
        });
    }

    private ToolResponse ExecuteAlignmentGet(ToolRequest request)
    {
        var parsed = ReadCivilArguments<CivilObjectGetRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var alignment = FindAlignment(input.Name, input.Handle, input.ObjectReference);
        return alignment is null
            ? Error(request, ErrorCode.AlignmentNotFound, "Alignment was not found in the target drawing.")
            : Success(request, new AlignmentGetResponse
            {
                InstanceId = Descriptor.InstanceId,
                DocumentId = Descriptor.ActiveDocument!.DocumentId,
                Alignment = alignment
            });
    }

    private ToolResponse ExecuteAlignmentStationOffset(ToolRequest request)
    {
        var parsed = ReadCivilArguments<AlignmentStationOffsetRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var alignment = FindAlignment(input.Name, input.Handle, input.ObjectReference);
        return alignment is null
            ? Error(request, ErrorCode.AlignmentNotFound, "Alignment was not found in the target drawing.")
            : Success(request, new AlignmentStationOffsetResponse
            {
                InstanceId = Descriptor.InstanceId,
                DocumentId = Descriptor.ActiveDocument!.DocumentId,
                Alignment = alignment,
                X = input.X,
                Y = input.Y,
                Station = 100,
                Offset = 12.5
            });
    }

    private ToolResponse ExecuteAlignmentPointLocation(ToolRequest request)
    {
        var parsed = ReadCivilArguments<AlignmentPointLocationRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var alignment = FindAlignment(input.Name, input.Handle, input.ObjectReference);
        return alignment is null
            ? Error(request, ErrorCode.AlignmentNotFound, "Alignment was not found in the target drawing.")
            : Success(request, new AlignmentPointLocationResponse
            {
                InstanceId = Descriptor.InstanceId,
                DocumentId = Descriptor.ActiveDocument!.DocumentId,
                Alignment = alignment,
                Station = input.Station,
                Offset = input.Offset,
                X = 1000,
                Y = 2000
            });
    }

    private ToolResponse ExecuteSurfaceList(ToolRequest request)
    {
        var parsed = ReadCivilArguments<CivilObjectListRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var surfaces = _surfaces
            .Where(item => string.IsNullOrWhiteSpace(input.NameContains) || item.Name.Contains(input.NameContains, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return Success(request, new SurfaceListResponse
        {
            InstanceId = Descriptor.InstanceId,
            DocumentId = Descriptor.ActiveDocument!.DocumentId,
            Count = surfaces.Length,
            Surfaces = surfaces
        });
    }

    private ToolResponse ExecuteSurfaceGet(ToolRequest request)
    {
        var parsed = ReadCivilArguments<CivilObjectGetRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var surface = FindSurface(input.Name, input.Handle, input.ObjectReference);
        return surface is null
            ? Error(request, ErrorCode.SurfaceNotFound, "Surface was not found in the target drawing.")
            : Success(request, new SurfaceGetResponse
            {
                InstanceId = Descriptor.InstanceId,
                DocumentId = Descriptor.ActiveDocument!.DocumentId,
                Surface = surface
            });
    }

    private ToolResponse ExecuteSurfaceElevationAtXy(ToolRequest request)
    {
        var parsed = ReadCivilArguments<SurfaceElevationAtXyRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var surface = FindSurface(input.Name, input.Handle, input.ObjectReference);
        return surface is null
            ? Error(request, ErrorCode.SurfaceNotFound, "Surface was not found in the target drawing.")
            : Success(request, new SurfaceElevationAtXyResponse
            {
                InstanceId = Descriptor.InstanceId,
                DocumentId = Descriptor.ActiveDocument!.DocumentId,
                Surface = surface,
                X = input.X,
                Y = input.Y,
                Elevation = 101.5
            });
    }

    private ToolResponse ExecuteCogoList(ToolRequest request)
    {
        var parsed = ReadCivilArguments<CogoListRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var points = _cogoPoints
            .Where(point => string.IsNullOrWhiteSpace(input.RawDescriptionContains) ||
                (point.RawDescription?.Contains(input.RawDescriptionContains, StringComparison.OrdinalIgnoreCase) ?? false))
            .Where(point => string.IsNullOrWhiteSpace(input.FullDescriptionContains) ||
                (point.FullDescription?.Contains(input.FullDescriptionContains, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(point => point.PointNumber)
            .ToArray();
        if (input.Limit is { } limit)
        {
            points = points.Take(limit).ToArray();
        }

        return Success(request, new CogoListResponse
        {
            InstanceId = Descriptor.InstanceId,
            DocumentId = Descriptor.ActiveDocument!.DocumentId,
            Count = points.Length,
            Points = points
        });
    }

    private ToolResponse ExecuteCogoGet(ToolRequest request)
    {
        var parsed = ReadCivilArguments<CogoGetRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var point = _cogoPoints.FirstOrDefault(candidate =>
            candidate.PointNumber == input.PointNumber ||
            string.Equals(candidate.Handle, input.Handle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Handle, input.ObjectReference?.Handle, StringComparison.OrdinalIgnoreCase));

        return point is null
            ? Error(request, ErrorCode.CogoPointNotFound, "COGO point was not found in the target drawing.")
            : Success(request, new CogoGetResponse
            {
                InstanceId = Descriptor.InstanceId,
                DocumentId = Descriptor.ActiveDocument!.DocumentId,
                Point = point
            });
    }

    private ToolResponse ExecutePropertySetDefinitionList(ToolRequest request)
    {
        var parsed = ReadCivilArguments<PropertySetDefinitionListRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var definitions = _propertySetDefinitions
            .Where(item => string.IsNullOrWhiteSpace(input.NameContains) ||
                item.Name.Contains(input.NameContains, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Success(request, new PropertySetDefinitionListResponse
        {
            InstanceId = Descriptor.InstanceId,
            DocumentId = Descriptor.ActiveDocument!.DocumentId,
            Count = definitions.Length,
            Definitions = definitions
        });
    }

    private ToolResponse ExecutePropertySetList(ToolRequest request)
    {
        var parsed = ReadCivilArguments<PropertySetListRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var handle = input.Handle ?? input.ObjectReference?.Handle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            return Error(request, ErrorCode.InvalidRequest, "propertyset_list requires handle or object_reference with a handle.");
        }

        var sourceSets = FindPropertySetsForObject(handle);
        if (sourceSets is null)
        {
            return Error(request, ErrorCode.ObjectNotFound, $"Object with handle '{handle}' was not found in the target drawing.");
        }

        var sets = sourceSets
            .Where(item => string.IsNullOrWhiteSpace(input.NameContains) ||
                item.Name.Contains(input.NameContains, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Success(request, new PropertySetListResponse
        {
            InstanceId = Descriptor.InstanceId,
            DocumentId = Descriptor.ActiveDocument!.DocumentId,
            ObjectHandle = handle,
            Count = sets.Length,
            PropertySets = sets
        });
    }

    private ToolResponse ExecutePropertySetGet(ToolRequest request)
    {
        var parsed = ReadCivilArguments<PropertySetGetRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var handle = input.Handle ?? input.ObjectReference?.Handle;
        if (string.IsNullOrWhiteSpace(handle))
        {
            return Error(request, ErrorCode.InvalidRequest, "propertyset_get requires handle or object_reference with a handle.");
        }

        var sets = FindPropertySetsForObject(handle);
        if (sets is null)
        {
            return Error(request, ErrorCode.ObjectNotFound, $"Object with handle '{handle}' was not found in the target drawing.");
        }

        var match = sets.FirstOrDefault(item => string.Equals(item.Name, input.Name, StringComparison.OrdinalIgnoreCase));
        return match is null
            ? Error(request, ErrorCode.PropertySetNotFound, $"Property set '{input.Name}' is not attached to object '{handle}'.")
            : Success(request, new PropertySetGetResponse
            {
                InstanceId = Descriptor.InstanceId,
                DocumentId = Descriptor.ActiveDocument!.DocumentId,
                ObjectHandle = handle,
                PropertySet = match
            });
    }

    private ToolResponse ExecutePropertySetNumericTestCreate(ToolRequest request)
    {
        var parsed = ReadCivilArguments<PropertySetNumericTestCreateRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        if (string.IsNullOrWhiteSpace(input.DefinitionName) ||
            !input.DefinitionName.StartsWith("MCP_NUMERIC_TEST", StringComparison.OrdinalIgnoreCase))
        {
            return Error(request, ErrorCode.InvalidRequest, "definition_name must start with MCP_NUMERIC_TEST.");
        }

        if (string.IsNullOrWhiteSpace(input.PropertyName))
        {
            return Error(request, ErrorCode.InvalidRequest, "property_name is required.");
        }

        if (!double.IsFinite(input.NumericValue))
        {
            return Error(request, ErrorCode.InvalidRequest, "numeric_value must be finite.");
        }

        var targetKind = string.IsNullOrWhiteSpace(input.TargetKind) ? "db_point" : input.TargetKind.Trim();
        if (!string.Equals(targetKind, "db_point", StringComparison.OrdinalIgnoreCase))
        {
            return Error(request, ErrorCode.InvalidRequest, "target_kind supports only db_point.");
        }

        const string objectHandle = "TMP1";
        var definitionHandle = $"PSD-{input.DefinitionName}";
        var propertySetHandle = $"PS-{objectHandle}";
        var numericValue = input.NumericValue.ToString("G17", CultureInfo.InvariantCulture);
        var definition = new PropertySetDefinitionInfo
        {
            Name = input.DefinitionName,
            Handle = definitionHandle,
            IsStyleBased = false,
            IsVisible = true,
            IsWriteable = true,
            AppliesToAll = false,
            AppliesTo = ["AcDbPoint"],
            PropertyCount = 1,
            Properties =
            [
                new PropertyDefinitionInfo { Name = input.PropertyName, DataType = "Real", IsReadOnly = false }
            ]
        };
        var propertySet = new PropertySetInfo
        {
            Name = input.DefinitionName,
            Handle = propertySetHandle,
            DefinitionHandle = definitionHandle,
            PropertyCount = 1,
            Properties =
            [
                new PropertyValueInfo
                {
                    Name = input.PropertyName,
                    Id = 1,
                    DataType = "Real",
                    Value = numericValue,
                    NumericValue = input.NumericValue
                }
            ]
        };

        _propertySetDefinitions.RemoveAll(item => string.Equals(item.Name, input.DefinitionName, StringComparison.OrdinalIgnoreCase));
        _propertySetDefinitions.Add(definition);
        _createdPropertySetsByObject[objectHandle] = [propertySet];

        return Success(request, new PropertySetNumericTestCreateResponse
        {
            InstanceId = Descriptor.InstanceId,
            DocumentId = Descriptor.ActiveDocument!.DocumentId,
            DefinitionName = input.DefinitionName,
            DefinitionHandle = definitionHandle,
            ObjectHandle = objectHandle,
            TargetKind = "db_point",
            PropertySet = propertySet,
            Transaction = new WriteTransactionInfo
            {
                DocumentLocked = true,
                TransactionStarted = true,
                Committed = true,
                RolledBack = false
            }
        });
    }

    private IReadOnlyList<PropertySetInfo>? FindPropertySetsForObject(string handle)
    {
        if (string.Equals(handle, "E1", StringComparison.OrdinalIgnoreCase))
        {
            return _propertySets;
        }

        return _createdPropertySetsByObject.TryGetValue(handle, out var sets) ? sets : null;
    }

    private ToolResponse ExecuteCorridorList(ToolRequest request)
    {
        var parsed = ReadCivilArguments<CivilObjectListRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var corridors = _corridors
            .Where(item => string.IsNullOrWhiteSpace(input.NameContains) ||
                item.Name.Contains(input.NameContains, StringComparison.OrdinalIgnoreCase))
            .Select(item => item with { Baselines = [] })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Success(request, new CorridorListResponse
        {
            InstanceId = Descriptor.InstanceId,
            DocumentId = Descriptor.ActiveDocument!.DocumentId,
            Count = corridors.Length,
            Corridors = corridors
        });
    }

    private ToolResponse ExecuteCorridorGet(ToolRequest request)
    {
        var parsed = ReadCivilArguments<CivilObjectGetRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var corridor = _corridors.FirstOrDefault(item =>
            string.Equals(item.Name, input.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Handle, input.Handle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Handle, input.ObjectReference?.Handle, StringComparison.OrdinalIgnoreCase));

        return corridor is null
            ? Error(request, ErrorCode.CorridorNotFound, "Corridor was not found in the target drawing.")
            : Success(request, new CorridorGetResponse
            {
                InstanceId = Descriptor.InstanceId,
                DocumentId = Descriptor.ActiveDocument!.DocumentId,
                Corridor = corridor
            });
    }

    private ToolResponse ExecuteProfileList(ToolRequest request)
    {
        var parsed = ReadCivilArguments<ProfileListRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var profiles = _profiles
            .Where(item => string.IsNullOrWhiteSpace(input.AlignmentName) ||
                string.Equals(item.AlignmentName, input.AlignmentName, StringComparison.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(input.NameContains) ||
                item.Name.Contains(input.NameContains, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.AlignmentName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Success(request, new ProfileListResponse
        {
            InstanceId = Descriptor.InstanceId,
            DocumentId = Descriptor.ActiveDocument!.DocumentId,
            Count = profiles.Length,
            Profiles = profiles
        });
    }

    private ToolResponse ExecuteProfileGet(ToolRequest request)
    {
        var parsed = ReadCivilArguments<ProfileGetRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var profile = FindFakeProfile(input.Name, input.Handle ?? input.ObjectReference?.Handle, input.AlignmentName);

        return profile is null
            ? Error(request, ErrorCode.ProfileNotFound, "Profile was not found in the target drawing.")
            : Success(request, new ProfileGetResponse
            {
                InstanceId = Descriptor.InstanceId,
                DocumentId = Descriptor.ActiveDocument!.DocumentId,
                Profile = profile
            });
    }

    private ToolResponse ExecuteProfileElevationAtStation(ToolRequest request)
    {
        var parsed = ReadCivilArguments<ProfileElevationAtStationRequest>(request);
        if (parsed is { Success: false })
        {
            return parsed.Response!;
        }

        var input = parsed.Value!;
        var profile = FindFakeProfile(input.Name, input.Handle ?? input.ObjectReference?.Handle, input.AlignmentName);

        return profile is null
            ? Error(request, ErrorCode.ProfileNotFound, "Profile was not found in the target drawing.")
            : Success(request, new ProfileElevationAtStationResponse
            {
                InstanceId = Descriptor.InstanceId,
                DocumentId = Descriptor.ActiveDocument!.DocumentId,
                Profile = profile,
                Station = input.Station,
                Elevation = 742.5,
                Grade = 0.025
            });
    }

    private ProfileInfo? FindFakeProfile(string? name, string? handle, string? alignmentName) =>
        _profiles.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(handle) && string.Equals(item.Handle, handle, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(name) &&
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(alignmentName) || string.Equals(item.AlignmentName, alignmentName, StringComparison.OrdinalIgnoreCase))));

    private LayerArgumentParse<T> ReadLayerArguments<T>(ToolRequest request)
    {
        if (request.Arguments is null)
        {
            return LayerArgumentParse<T>.Failure(Error(request, ErrorCode.InvalidRequest, "Layer tool request requires structured arguments."));
        }

        var input = request.Arguments.Deserialize<T>(JsonContractOptions.Default);
        var instanceId = ReadString(request.Arguments, "instance_id", "instanceId");
        var documentId = ReadString(request.Arguments, "document_id", "documentId");
        if (!string.Equals(instanceId, Descriptor.InstanceId, StringComparison.Ordinal))
        {
            return LayerArgumentParse<T>.Failure(Error(request, ErrorCode.InstanceNotFound, "Layer request instance_id does not match this fake bridge."));
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return LayerArgumentParse<T>.Failure(Error(request, ErrorCode.DocumentAmbiguous, "document_id is required for layer tools."));
        }

        if (!string.Equals(documentId, Descriptor.ActiveDocument?.DocumentId, StringComparison.Ordinal))
        {
            return LayerArgumentParse<T>.Failure(Error(request, ErrorCode.DocumentNotFound, "Target drawing document was not found in this fake bridge."));
        }

        return LayerArgumentParse<T>.Ok(input!);
    }

    private LayerArgumentParse<T> ReadCivilArguments<T>(ToolRequest request)
    {
        if (request.Arguments is null)
        {
            return LayerArgumentParse<T>.Failure(Error(request, ErrorCode.InvalidRequest, "Civil 3D tool request requires structured arguments."));
        }

        var input = request.Arguments.Deserialize<T>(JsonContractOptions.Default);
        var instanceId = ReadString(request.Arguments, "instance_id", "instanceId");
        var documentId = ReadString(request.Arguments, "document_id", "documentId");
        if (!string.Equals(instanceId, Descriptor.InstanceId, StringComparison.Ordinal))
        {
            return LayerArgumentParse<T>.Failure(Error(request, ErrorCode.InstanceNotFound, "Civil 3D request instance_id does not match this fake bridge."));
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            return LayerArgumentParse<T>.Failure(Error(request, ErrorCode.DocumentAmbiguous, "document_id is required for Civil 3D tools."));
        }

        if (!string.Equals(documentId, Descriptor.ActiveDocument?.DocumentId, StringComparison.Ordinal))
        {
            return LayerArgumentParse<T>.Failure(Error(request, ErrorCode.DocumentNotFound, "Target drawing document was not found in this fake bridge."));
        }

        return LayerArgumentParse<T>.Ok(input!);
    }

    private AlignmentInfo? FindAlignment(string? name, string? handle, CadObjectReference? reference) =>
        _alignments.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Handle, handle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Name, reference?.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Handle, reference?.Handle, StringComparison.OrdinalIgnoreCase));

    private SurfaceInfo? FindSurface(string? name, string? handle, CadObjectReference? reference) =>
        _surfaces.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Handle, handle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Name, reference?.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Handle, reference?.Handle, StringComparison.OrdinalIgnoreCase));

    private LayerInfo? FindLayer(string? name, string? handle, CadObjectReference? reference) =>
        _layers.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Handle, handle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Name, reference?.Name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Handle, reference?.Handle, StringComparison.OrdinalIgnoreCase));

    private ToolResponse Success(ToolRequest request, object data) =>
        new()
        {
            Success = true,
            Tool = request.ToolName,
            CorrelationId = request.CorrelationId,
            Document = Descriptor.ActiveDocument,
            Data = JsonContractOptions.ToJsonNode(data)
        };

    private ToolResponse Error(ToolRequest request, ErrorCode code, string message) =>
        new()
        {
            Success = false,
            Tool = request.ToolName,
            CorrelationId = request.CorrelationId,
            Document = Descriptor.ActiveDocument,
            Error = new ErrorEnvelope
            {
                Code = code,
                Message = message,
                CorrelationId = request.CorrelationId
            }
        };

    private static IReadOnlyList<ToolDescriptor> CreateTools(TimeSpan timeout) =>
    [
        Tool("civil3d_ping", "Core", "PingResponse", timeout),
        Tool("civil3d_status", "Diagnostics", "StatusResponse", timeout),
        Tool("document_list", "Core", "DocumentListResponse", timeout),
        Tool("list_dialogs", "Diagnostics", "DialogListResponse", timeout),
        Tool("dialog_decision_list", "Diagnostics", "DialogDecisionListResponse", timeout),
        DiagnosticsWriteTool("dialog_decision_set", "DialogDecisionSetResponse", timeout, ToolRisk.Low, RollbackSemantics.NoneRequired),
        DiagnosticsWriteTool("dismiss_dialog", "DialogDismissResponse", timeout, ToolRisk.Medium, RollbackSemantics.NotSupported),
        RoadwayTool("roadway_station_offset_batch", "RoadwayStationOffsetBatchResponse", timeout),
        RoadwayWriteTool("roadway_propertyset_write", "RoadwayPropertySetWriteResponse", timeout),
        Tool("drawing_info", "Core", "DrawingInfoResponse", timeout),
        LayerTool("layer_list", "LayerListResponse", LayerListInputSchema(), timeout),
        LayerTool("layer_get", "LayerGetResponse", LayerGetInputSchema(), timeout),
        LayerTool("layer_create", "LayerCreateResponse", LayerCreateInputSchema(), timeout, OperationClass.Write, RollbackSemantics.TransactionRollbackBeforeCommit),
        LayerTool("layer_update", "LayerUpdateResponse", LayerUpdateInputSchema(), timeout, OperationClass.Write, RollbackSemantics.TransactionRollbackBeforeCommit),
        LayerTool("layer_set_current", "LayerSetCurrentResponse", LayerSetCurrentInputSchema(), timeout, OperationClass.Write, RollbackSemantics.TransactionRollbackBeforeCommit),
        LayerTool("object_get", "ObjectGetResponse", ObjectGetInputSchema(), timeout),
        LayerTool("object_properties", "ObjectPropertiesResponse", ObjectPropertiesInputSchema(), timeout),
        LayerTool("block_list", "BlockListResponse", BlockListInputSchema(), timeout),
        LayerTool("block_reference_list", "BlockReferenceListResponse", BlockReferenceListInputSchema(), timeout),
        LayerTool("block_get_attributes", "BlockGetAttributesResponse", BlockGetAttributesInputSchema(), timeout),
        CivilTool("alignment_list", "AlignmentListResponse", CivilNamedListInputSchema(), timeout),
        CivilTool("alignment_get", "AlignmentGetResponse", CivilNamedGetInputSchema(), timeout),
        CivilTool("alignment_station_offset", "AlignmentStationOffsetResponse", CivilAlignmentStationOffsetInputSchema(), timeout),
        CivilTool("alignment_point_location", "AlignmentPointLocationResponse", CivilAlignmentPointLocationInputSchema(), timeout),
        CivilTool("surface_list", "SurfaceListResponse", CivilNamedListInputSchema(), timeout),
        CivilTool("surface_get", "SurfaceGetResponse", CivilNamedGetInputSchema(), timeout),
        CivilTool("surface_elevation_at_xy", "SurfaceElevationAtXyResponse", CivilSurfaceElevationInputSchema(), timeout),
        CivilTool("cogo_list", "CogoListResponse", CivilCogoListInputSchema(), timeout),
        CivilTool("cogo_get", "CogoGetResponse", CivilCogoGetInputSchema(), timeout),
        CivilTool("profile_list", "ProfileListResponse", CivilProfileListInputSchema(), timeout),
        CivilTool("profile_get", "ProfileGetResponse", CivilProfileGetInputSchema(), timeout),
        CivilTool("profile_elevation_at_station", "ProfileElevationAtStationResponse", CivilProfileElevationAtStationInputSchema(), timeout),
        CivilTool("corridor_list", "CorridorListResponse", CivilNamedListInputSchema(), timeout),
        CivilTool("corridor_get", "CorridorGetResponse", CivilNamedGetInputSchema(), timeout),
        CivilTool("propertyset_definition_list", "PropertySetDefinitionListResponse", CivilNamedListInputSchema(), timeout),
        CivilTool("propertyset_list", "PropertySetListResponse", CivilPropertySetListInputSchema(), timeout),
        CivilTool("propertyset_get", "PropertySetGetResponse", CivilPropertySetGetInputSchema(), timeout),
        CivilTool("propertyset_find", "PropertySetFindResponse", CivilNamedListInputSchema(), timeout),
        CivilTool("propertyset_numeric_test_create", "PropertySetNumericTestCreateResponse", CivilPropertySetNumericTestCreateInputSchema(), timeout, OperationClass.Write, RollbackSemantics.TransactionRollbackBeforeCommit),
        CivilTool("propertyset_set", "PropertySetUpdateResponse", CivilNamedGetInputSchema(), timeout, OperationClass.Write, RollbackSemantics.TransactionRollbackBeforeCommit, ToolRisk.Medium, requiresConfirmation: true),
        SurveyTool("survey_bearing_to_azimuth", "SurveyBearingToAzimuthResponse", SurveyBearingToAzimuthInputSchema(), timeout),
        SurveyTool("survey_azimuth_to_bearing", "SurveyAzimuthToBearingResponse", SurveyAzimuthToBearingInputSchema(), timeout),
        SurveyTool("survey_distance", "SurveyDistanceResponse", SurveyDistanceInputSchema(), timeout),
        SurveyTool("survey_point_by_azimuth", "SurveyPointByAzimuthResponse", SurveyPointByAzimuthInputSchema(), timeout),
        SurveyTool("survey_fbk_dry_run", "SurveyFbkDryRunResponse", SurveyFbkDryRunInputSchema(), timeout),
        SurveyTool("survey_database_status", "SurveyDatabaseStatusResponse", SurveyDatabaseStatusInputSchema(), timeout),
        SurveyTool("survey_working_folder_get", "SurveyWorkingFolderGetResponse", SurveyDatabaseStatusInputSchema(), TimeSpan.FromSeconds(10)),
        SurveyTool("survey_project_list", "SurveyProjectListResponse", SurveyDatabaseStatusInputSchema(), TimeSpan.FromSeconds(10)),
        SurveyTool("survey_network_list", "SurveyNetworkListResponse", SurveyNetworkListInputSchema(), TimeSpan.FromSeconds(10))
    ];

    /// <summary>Mirrors the real bridge, where the dialog decision tools are WRITE.</summary>
    private static ToolDescriptor DiagnosticsWriteTool(
        string name,
        string outputTitle,
        TimeSpan timeout,
        ToolRisk risk,
        RollbackSemantics rollbackSemantics) =>
        new()
        {
            Name = name,
            Description = $"Fake {name}",
            OperationClass = OperationClass.Write,
            Risk = risk,
            InputSchema = ToolSchema.EmptyInput(),
            OutputSchema = ToolSchema.ObjectOutput(outputTitle),
            Timeout = timeout,
            RequiresConfirmation = false,
            RollbackSemantics = rollbackSemantics,
            Module = "Diagnostics",
            Version = ProtocolVersion.Current
        };

    private static ToolDescriptor Tool(string name, string module, string outputTitle, TimeSpan timeout) =>
        new()
        {
            Name = name,
            Description = $"Fake {name}",
            OperationClass = OperationClass.Read,
            Risk = ToolRisk.Low,
            InputSchema = ToolSchema.EmptyInput(),
            OutputSchema = ToolSchema.ObjectOutput(outputTitle),
            Timeout = timeout,
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.NoneRequired,
            Module = module,
            Version = ProtocolVersion.Current
        };

    private static ToolDescriptor LayerTool(
        string name,
        string outputTitle,
        JsonObject inputSchema,
        TimeSpan timeout,
        OperationClass operationClass = OperationClass.Read,
        RollbackSemantics rollbackSemantics = RollbackSemantics.NoneRequired) =>
        new()
        {
            Name = name,
            Description = $"Fake {name}",
            OperationClass = operationClass,
            Risk = ToolRisk.Low,
            InputSchema = inputSchema,
            OutputSchema = ToolSchema.ObjectOutput(outputTitle),
            Timeout = timeout,
            RequiresConfirmation = false,
            RollbackSemantics = rollbackSemantics,
            Module = "AutoCAD",
            Version = ProtocolVersion.Current
        };

    private static ToolDescriptor CivilTool(
        string name,
        string outputTitle,
        JsonObject inputSchema,
        TimeSpan timeout,
        OperationClass operationClass = OperationClass.Read,
        RollbackSemantics rollbackSemantics = RollbackSemantics.NoneRequired,
        ToolRisk risk = ToolRisk.Low,
        bool requiresConfirmation = false) =>
        new()
        {
            Name = name,
            Description = $"Fake {name}",
            OperationClass = operationClass,
            Risk = risk,
            InputSchema = inputSchema,
            OutputSchema = ToolSchema.ObjectOutput(outputTitle),
            Timeout = timeout,
            RequiresConfirmation = requiresConfirmation,
            RollbackSemantics = rollbackSemantics,
            Module = "Civil3D",
            Version = ProtocolVersion.Current
        };

    private static ToolDescriptor RoadwayWriteTool(string name, string outputTitle, TimeSpan timeout) =>
        new()
        {
            Name = name,
            Description = $"Fake {name}",
            OperationClass = OperationClass.Write,
            Risk = ToolRisk.Medium,
            InputSchema = ToolSchema.EmptyInput(),
            OutputSchema = ToolSchema.ObjectOutput(outputTitle),
            Timeout = timeout,
            RequiresConfirmation = true,
            RollbackSemantics = RollbackSemantics.TransactionRollbackBeforeCommit,
            Module = "Roadway",
            Version = ProtocolVersion.Current
        };

    private static ToolDescriptor RoadwayTool(string name, string outputTitle, TimeSpan timeout) =>
        new()
        {
            Name = name,
            Description = $"Fake {name}",
            OperationClass = OperationClass.Read,
            Risk = ToolRisk.Low,
            InputSchema = ToolSchema.EmptyInput(),
            OutputSchema = ToolSchema.ObjectOutput(outputTitle),
            Timeout = timeout,
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.NoneRequired,
            Module = "Roadway",
            Version = ProtocolVersion.Current
        };

    private static ToolDescriptor SurveyTool(
        string name,
        string outputTitle,
        JsonObject inputSchema,
        TimeSpan timeout) =>
        new()
        {
            Name = name,
            Description = $"Fake {name}",
            OperationClass = OperationClass.Read,
            Risk = ToolRisk.Low,
            InputSchema = inputSchema,
            OutputSchema = ToolSchema.ObjectOutput(outputTitle),
            Timeout = timeout,
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.NoneRequired,
            Module = "Survey",
            Version = ProtocolVersion.Current
        };

    private static JsonObject LayerListInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["name_contains"] = new JsonObject { ["type"] = "string" },
            ["is_on"] = new JsonObject { ["type"] = "boolean" },
            ["is_frozen"] = new JsonObject { ["type"] = "boolean" },
            ["is_locked"] = new JsonObject { ["type"] = "boolean" },
            ["is_plottable"] = new JsonObject { ["type"] = "boolean" }
        });

    /// <summary>Handle of the single object the fake bridge knows about.</summary>
    private const string FakeObjectHandle = "B2";

    private CadObjectInfo FakeObject() =>
        new()
        {
            Handle = FakeObjectHandle,
            ObjectId = "(2130843560256)",
            ObjectClass = "AcDbLine",
            DxfName = "LINE",
            AppName = "ObjectDBX Classes",
            ClassHierarchy = ["AcDbCurve", "AcDbEntity", "AcDbObject"],
            IsEntity = true,
            IsErased = false,
            IsProxy = false,
            IsPersistent = true,
            OwnerHandle = "1F",
            OwnerClass = "AcDbBlockTableRecord",
            ObjectReference = new CadObjectReference
            {
                InstanceId = Descriptor.InstanceId,
                DocumentId = Descriptor.ActiveDocument!.DocumentId,
                DatabaseFingerprint = Descriptor.ActiveDocument.DatabaseFingerprint,
                Handle = FakeObjectHandle,
                ObjectId = "(2130843560256)",
                Type = "AcDbLine",
                Name = null
            },
            Entity = new CadEntityInfo
            {
                Layer = "0",
                Linetype = "ByLayer",
                LinetypeScale = 1.0,
                Lineweight = "ByLayer",
                ColorIndex = 256,
                Color = "ByLayer",
                TransparencyAlpha = 255,
                IsVisible = true,
                BlockName = "*Model_Space",
                Extents = new CadExtentsInfo
                {
                    MinX = 0,
                    MinY = 0,
                    MinZ = 0,
                    MaxX = 100,
                    MaxY = 50,
                    MaxZ = 0
                }
            }
        };

    private static IReadOnlyList<ObjectPropertyInfo> FakeObjectProperties() =>
    [
        new()
        {
            Name = "Length",
            DeclaringType = "Line",
            DataType = "Double",
            Value = "111.80339887498948",
            NumericValue = 111.80339887498948,
            IsReadOnly = true
        },
        new()
        {
            Name = "Layer",
            DeclaringType = "Entity",
            DataType = "String",
            Value = "0",
            NumericValue = null,
            IsReadOnly = false
        },
        new()
        {
            // The host refused this one; the row survives so the caller can see why.
            Name = "Normal",
            DeclaringType = "Line",
            DataType = "Vector3d",
            Value = null,
            NumericValue = null,
            IsReadOnly = true,
            Error = "eNotApplicable"
        }
    ];

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim();

    private static JsonObject ObjectGetInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["handle"] = new JsonObject { ["type"] = "string" },
            ["object_reference"] = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false
            }
        });

    private static JsonObject BlockListInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["name_contains"] = new JsonObject { ["type"] = "string" },
            ["include_anonymous"] = new JsonObject { ["type"] = "boolean" },
            ["include_layouts"] = new JsonObject { ["type"] = "boolean" }
        });

    private static JsonObject BlockReferenceListInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["block_name"] = new JsonObject { ["type"] = "string" },
            ["layer"] = new JsonObject { ["type"] = "string" },
            ["include_anonymous"] = new JsonObject { ["type"] = "boolean" },
            ["include_attributes"] = new JsonObject { ["type"] = "boolean" },
            ["max_scan"] = new JsonObject { ["type"] = "integer" },
            ["max_results"] = new JsonObject { ["type"] = "integer" }
        });

    private static JsonObject BlockGetAttributesInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["handle"] = new JsonObject { ["type"] = "string" },
            ["object_reference"] = new JsonObject { ["type"] = "object" }
        });

    private static JsonObject ObjectPropertiesInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["handle"] = new JsonObject { ["type"] = "string" },
            ["object_reference"] = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false
            },
            ["name_contains"] = new JsonObject { ["type"] = "string" },
            ["include_unreadable"] = new JsonObject { ["type"] = "boolean" }
        });

    private static JsonObject LayerGetInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string" },
            ["handle"] = new JsonObject { ["type"] = "string" },
            ["object_reference"] = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false
            }
        });

    private static JsonObject LayerCreateInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string" },
            ["color_index"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 255 },
            ["is_plottable"] = new JsonObject { ["type"] = "boolean" },
            ["description"] = new JsonObject { ["type"] = "string" }
        }, requiredExtraProperties: ["name"]);

    private static JsonObject LayerUpdateInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string" },
            ["handle"] = new JsonObject { ["type"] = "string" },
            ["object_reference"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false },
            ["new_name"] = new JsonObject { ["type"] = "string" },
            ["color_index"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 255 },
            ["is_on"] = new JsonObject { ["type"] = "boolean" },
            ["is_frozen"] = new JsonObject { ["type"] = "boolean" },
            ["is_locked"] = new JsonObject { ["type"] = "boolean" },
            ["is_plottable"] = new JsonObject { ["type"] = "boolean" },
            ["description"] = new JsonObject { ["type"] = "string" }
        });

    private static JsonObject LayerSetCurrentInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string" },
            ["handle"] = new JsonObject { ["type"] = "string" },
            ["object_reference"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false }
        });

    private static JsonObject DocumentTargetSchema(JsonObject extraProperties, IReadOnlyList<string>? requiredExtraProperties = null)
    {
        var properties = new JsonObject
        {
            ["instance_id"] = new JsonObject { ["type"] = "string" },
            ["document_id"] = new JsonObject { ["type"] = "string" }
        };

        foreach (var property in extraProperties)
        {
            properties[property.Key] = property.Value?.DeepClone();
        }

        var required = new JsonArray("instance_id", "document_id");
        foreach (var property in requiredExtraProperties ?? Array.Empty<string>())
        {
            required.Add(property);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = required,
            ["properties"] = properties
        };
    }

    private static JsonObject CivilNamedListInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["name_contains"] = new JsonObject { ["type"] = "string" }
        });

    private static JsonObject CivilPropertySetListInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["handle"] = new JsonObject { ["type"] = "string" },
            ["object_reference"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false },
            ["name_contains"] = new JsonObject { ["type"] = "string" }
        });

    private static JsonObject CivilPropertySetGetInputSchema() =>
        DocumentTargetSchema(
            new JsonObject
            {
                ["handle"] = new JsonObject { ["type"] = "string" },
                ["object_reference"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false },
                ["name"] = new JsonObject { ["type"] = "string" }
            },
            ["name"]);

    private static JsonObject CivilPropertySetNumericTestCreateInputSchema() =>
        DocumentTargetSchema(
            new JsonObject
            {
                ["definition_name"] = new JsonObject { ["type"] = "string" },
                ["property_name"] = new JsonObject { ["type"] = "string" },
                ["numeric_value"] = new JsonObject { ["type"] = "number" },
                ["target_kind"] = new JsonObject { ["type"] = "string" }
            },
            ["definition_name", "property_name", "numeric_value"]);

    private static JsonObject CivilProfileListInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["alignment_name"] = new JsonObject { ["type"] = "string" },
            ["name_contains"] = new JsonObject { ["type"] = "string" }
        });

    private static JsonObject CivilProfileGetInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string" },
            ["handle"] = new JsonObject { ["type"] = "string" },
            ["object_reference"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false },
            ["alignment_name"] = new JsonObject { ["type"] = "string" }
        });

    private static JsonObject CivilProfileElevationAtStationInputSchema() =>
        DocumentTargetSchema(
            new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string" },
                ["handle"] = new JsonObject { ["type"] = "string" },
                ["object_reference"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false },
                ["alignment_name"] = new JsonObject { ["type"] = "string" },
                ["station"] = new JsonObject { ["type"] = "number" }
            },
            ["station"]);

    private static JsonObject CivilNamedGetInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string" },
            ["handle"] = new JsonObject { ["type"] = "string" },
            ["object_reference"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false }
        });

    private static JsonObject CivilAlignmentStationOffsetInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string" },
            ["handle"] = new JsonObject { ["type"] = "string" },
            ["object_reference"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false },
            ["x"] = new JsonObject { ["type"] = "number" },
            ["y"] = new JsonObject { ["type"] = "number" }
        }, requiredExtraProperties: ["x", "y"]);

    private static JsonObject CivilAlignmentPointLocationInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string" },
            ["handle"] = new JsonObject { ["type"] = "string" },
            ["object_reference"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false },
            ["station"] = new JsonObject { ["type"] = "number" },
            ["offset"] = new JsonObject { ["type"] = "number" }
        }, requiredExtraProperties: ["station"]);

    private static JsonObject CivilSurfaceElevationInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string" },
            ["handle"] = new JsonObject { ["type"] = "string" },
            ["object_reference"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false },
            ["x"] = new JsonObject { ["type"] = "number" },
            ["y"] = new JsonObject { ["type"] = "number" }
        }, requiredExtraProperties: ["x", "y"]);

    private static JsonObject CivilCogoListInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["raw_description_contains"] = new JsonObject { ["type"] = "string" },
            ["full_description_contains"] = new JsonObject { ["type"] = "string" },
            ["limit"] = new JsonObject { ["type"] = "integer" }
        });

    private static JsonObject CivilCogoGetInputSchema() =>
        DocumentTargetSchema(new JsonObject
        {
            ["point_number"] = new JsonObject { ["type"] = "integer" },
            ["handle"] = new JsonObject { ["type"] = "string" },
            ["object_reference"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false }
        });

    private static JsonObject SurveyBearingToAzimuthInputSchema() =>
        SurveyObjectSchema(new JsonObject
        {
            ["bearing"] = new JsonObject { ["type"] = "string" }
        }, ["bearing"]);

    private static JsonObject SurveyAzimuthToBearingInputSchema() =>
        SurveyObjectSchema(new JsonObject
        {
            ["azimuth_degrees"] = new JsonObject { ["type"] = "number" }
        }, ["azimuth_degrees"]);

    private static JsonObject SurveyDistanceInputSchema() =>
        SurveyObjectSchema(new JsonObject
        {
            ["start_x"] = new JsonObject { ["type"] = "number" },
            ["start_y"] = new JsonObject { ["type"] = "number" },
            ["end_x"] = new JsonObject { ["type"] = "number" },
            ["end_y"] = new JsonObject { ["type"] = "number" }
        }, ["start_x", "start_y", "end_x", "end_y"]);

    private static JsonObject SurveyPointByAzimuthInputSchema() =>
        SurveyObjectSchema(new JsonObject
        {
            ["start_x"] = new JsonObject { ["type"] = "number" },
            ["start_y"] = new JsonObject { ["type"] = "number" },
            ["azimuth_degrees"] = new JsonObject { ["type"] = "number" },
            ["distance"] = new JsonObject { ["type"] = "number" }
        }, ["start_x", "start_y", "azimuth_degrees", "distance"]);

    private static JsonObject SurveyFbkDryRunInputSchema() =>
        SurveyObjectSchema(new JsonObject
        {
            ["source_name"] = new JsonObject { ["type"] = "string" },
            ["content"] = new JsonObject { ["type"] = "string" }
        }, ["content"]);

    private static JsonObject SurveyDatabaseStatusInputSchema() =>
        SurveyObjectSchema(new JsonObject(), []);

    private static JsonObject SurveyNetworkListInputSchema() =>
        SurveyObjectSchema(
            new JsonObject
            {
                ["project_name"] = new JsonObject { ["type"] = "string" }
            },
            []);

    private static JsonObject SurveyObjectSchema(JsonObject properties, IReadOnlyList<string> required) =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray(required.Select(item => JsonValue.Create(item)).ToArray<JsonNode?>()),
            ["properties"] = properties
        };

    private static IReadOnlyList<LayerInfo> CreateLayers(string instanceId, string documentId) =>
    [
        CreateLayer(instanceId, documentId, "0", "10", isCurrent: true),
        CreateLayer(instanceId, documentId, "Defpoints", "11", isPlottable: false),
        CreateLayer(instanceId, documentId, "C-ROAD-CNTR", "12", isLocked: true)
    ];

    private static LayerInfo CreateLayer(
        string instanceId,
        string documentId,
        string name,
        string handle,
        bool isCurrent = false,
        bool isLocked = false,
        bool isPlottable = true) =>
        new()
        {
            Name = name,
            Handle = handle,
            ObjectReference = new CadObjectReference
            {
                InstanceId = instanceId,
                DocumentId = documentId,
                DatabaseFingerprint = $"fingerprint-{instanceId}",
                Handle = handle,
                ObjectId = $"({handle})",
                Type = "LayerTableRecord",
                Name = name
            },
            IsCurrent = isCurrent,
            IsOn = true,
            IsFrozen = false,
            IsLocked = isLocked,
            IsPlottable = isPlottable,
            Color = new LayerColorInfo
            {
                ColorIndex = 7,
                Method = "ByAci"
            },
            Linetype = "Continuous",
            Lineweight = "ByLineWeightDefault"
        };

    private static IReadOnlyList<AlignmentInfo> CreateAlignments(string instanceId, string documentId) =>
    [
        CreateAlignment(instanceId, documentId, "MAIN_ROAD", "A1"),
        CreateAlignment(instanceId, documentId, "SIDE_ROAD", "A2")
    ];

    private static AlignmentInfo CreateAlignment(string instanceId, string documentId, string name, string handle) =>
        new()
        {
            Name = name,
            Handle = handle,
            ObjectReference = CreateReference(instanceId, documentId, name, handle, "Alignment"),
            Type = "Centerline",
            Length = 500,
            StartingStation = 0,
            EndingStation = 500
        };

    private static IReadOnlyList<SurfaceInfo> CreateSurfaces(string instanceId, string documentId) =>
    [
        new()
        {
            Name = "EG",
            Handle = "B1",
            ObjectReference = CreateReference(instanceId, documentId, "EG", "B1", "TinSurface"),
            Type = "TinSurface",
            PointCount = 10,
            MinimumElevation = 100,
            MaximumElevation = 120,
            MeanElevation = 110
        }
    ];

    private static IReadOnlyList<CogoPointInfo> CreateCogoPoints(string instanceId, string documentId) =>
    [
        new()
        {
            PointNumber = 1,
            Handle = "C1",
            ObjectReference = CreateReference(instanceId, documentId, "1", "C1", "CogoPoint"),
            Easting = 1000,
            Northing = 2000,
            Elevation = 50,
            RawDescription = "TREE",
            FullDescription = "Tree",
            IsLocked = false
        }
    ];

    private static CadObjectReference CreateReference(string instanceId, string documentId, string name, string handle, string type) =>
        new()
        {
            InstanceId = instanceId,
            DocumentId = documentId,
            DatabaseFingerprint = $"fingerprint-{instanceId}",
            Handle = handle,
            ObjectId = $"({handle})",
            Type = type,
            Name = name
        };

    private static string? ReadString(JsonNode node, params string[] names)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (obj.TryGetPropertyValue(name, out var value) && value is not null)
            {
                try
                {
                    var text = value.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
                catch
                {
                    return null;
                }
            }
        }

        return null;
    }

    private static T ReadSurveyArguments<T>(ToolRequest request)
    {
        if (request.Arguments is null)
        {
            throw new SurveyToolException(ErrorCode.InvalidRequest, "Survey tool request requires structured arguments.");
        }

        return request.Arguments.Deserialize<T>(JsonContractOptions.Default)
            ?? throw new SurveyToolException(ErrorCode.InvalidRequest, "Survey tool request arguments could not be read.");
    }

    private static T ReadOptionalSurveyArguments<T>(ToolRequest request)
        where T : new()
    {
        if (request.Arguments is null)
        {
            return new T();
        }

        return request.Arguments.Deserialize<T>(JsonContractOptions.Default)
            ?? throw new SurveyToolException(ErrorCode.InvalidRequest, "Survey tool request arguments could not be read.");
    }

    private static CapabilityManifest CreateCapabilities(
        string instanceId,
        ProtocolVersion protocolVersion,
        IReadOnlyList<ToolDescriptor> tools) =>
        new()
        {
            InstanceId = instanceId,
            Product = "Autodesk Civil 3D",
            ProductVersion = "2026",
            BridgeVersion = ProtocolConstants.BridgeVersion,
            ProtocolVersion = protocolVersion,
            ToolManifestVersion = ProtocolVersion.Current,
            Capabilities = new CapabilityFlags
            {
                Core = true,
                AutoCad = true,
                Civil3D = true,
                Diagnostics = true,
                DialogMonitor = "partial",
                Survey = true,
                SurveyCom = true,
                DeveloperMode = false
            },
            FeatureFlags = new Dictionary<string, bool>
            {
                ["confirmation_tokens"] = true,
                ["named_pipe_current_user_only"] = true,
                ["survey_enabled"] = true,
                ["survey_geometry_enabled"] = true,
                ["survey_fbk_dry_run_enabled"] = true,
                ["survey_database_status_enabled"] = true,
                ["survey_working_folder_get_enabled"] = true,
                ["survey_project_list_enabled"] = true,
                ["survey_network_list_enabled"] = true
            },
            Tools = tools
        };

    private static DocumentDescriptor CreateDocument(string instanceId) =>
        new()
        {
            InstanceId = instanceId,
            DocumentId = $"doc-{instanceId}",
            Name = "FakeProject.dwg",
            FullPath = "C:\\Civil3DHybridMcp\\FakeProject.dwg",
            IsActive = true,
            IsSaved = true,
            IsReadOnly = false,
            Units = "Meters",
            DatabaseFingerprint = $"fingerprint-{instanceId}",
            SessionIdentifier = $"doc-session-{instanceId}"
        };

    private sealed record LayerArgumentParse<T>(bool Success, T? Value, ToolResponse? Response)
    {
        public static LayerArgumentParse<T> Ok(T value) => new(true, value, null);
        public static LayerArgumentParse<T> Failure(ToolResponse response) => new(false, default, response);
    }

    private sealed class FakeSurveyDatabaseGateway : ISurveyDatabaseGateway
    {
        public SurveyDatabaseStatusResponse GetStatus(string instanceId) =>
            new()
            {
                InstanceId = instanceId,
                SurveyComAvailable = true,
                SurveyDatabaseAvailable = false,
                RequiresActiveDocument = true,
                Adapter = "fake",
                Status = "interop_available",
                InteropAssemblies = ["Autodesk.AECC.Interop.Survey.dll"],
                Capabilities = ["working_folder_read", "project_list_read"],
                Limitations = ["Fake bridge does not connect to Civil 3D Survey COM."]
            };

        public SurveyWorkingFolderGetResponse GetWorkingFolder(string instanceId) =>
            new()
            {
                InstanceId = instanceId,
                Adapter = "fake",
                Status = "working_folder_available",
                WorkingFolder = @"C:\SurveyData",
                RequiresActiveDocument = true,
                Capabilities = ["working_folder_read"],
                Limitations = ["Fake bridge response."]
            };

        public SurveyProjectListResponse ListProjects(string instanceId) =>
            new()
            {
                InstanceId = instanceId,
                Adapter = "fake",
                Status = "projects_available",
                WorkingFolder = @"C:\SurveyData",
                Count = 2,
                Projects =
                [
                    new SurveyProjectInfo
                    {
                        Name = "Topo-2026",
                        Path = @"C:\SurveyData\Topo-2026",
                        ProjectGuid = "{11111111-1111-1111-1111-111111111111}"
                    },
                    new SurveyProjectInfo
                    {
                        Name = "Control",
                        Path = @"C:\SurveyData\Control",
                        ProjectGuid = "{22222222-2222-2222-2222-222222222222}"
                    }
                ],
                RequiresActiveDocument = true,
                Capabilities = ["working_folder_read", "project_list_read"],
                Limitations = ["Fake bridge response."]
            };

        public SurveyNetworkListResponse ListNetworks(string instanceId, string? projectName)
        {
            var networks = new List<SurveyNetworkInfo>
            {
                new()
                {
                    Name = "NET-CONTROLE",
                    ProjectName = "Topo-2026",
                    Description = "Rede de controle",
                    Path = @"C:\SurveyData\Topo-2026\Survey\NET-CONTROLE",
                    Id = 1
                },
                new()
                {
                    Name = "NET-APOIO",
                    ProjectName = "Topo-2026",
                    Description = null,
                    Path = @"C:\SurveyData\Topo-2026\Survey\NET-APOIO",
                    Id = 2
                },
                new()
                {
                    Name = "NET-MARCOS",
                    ProjectName = "Control",
                    Description = "Marcos geodesicos",
                    Path = @"C:\SurveyData\Control\Survey\NET-MARCOS",
                    Id = 3
                }
            };

            if (!string.IsNullOrWhiteSpace(projectName))
            {
                networks = networks
                    .Where(network => string.Equals(network.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return new SurveyNetworkListResponse
            {
                InstanceId = instanceId,
                Adapter = "fake",
                Status = "networks_available",
                ProjectName = string.IsNullOrWhiteSpace(projectName) ? null : projectName,
                WorkingFolder = @"C:\SurveyData",
                Count = networks.Count,
                Networks = networks,
                RequiresActiveDocument = true,
                Capabilities = ["working_folder_read", "project_list_read", "network_list_read"],
                Limitations = ["Fake bridge response."]
            };
        }
    }
}
