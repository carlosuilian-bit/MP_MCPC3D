using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Diagnostics;
using Civil3D.HybridMcp.Protocol;
using Civil3D.HybridMcp.Sdk;
using Civil3D.HybridMcp.Tools.AutoCAD;
using Civil3D.HybridMcp.Tools.Civil;
using Civil3D.HybridMcp.Tools.Roadway;
using Civil3D.HybridMcp.Tools.Survey;
using AcadApplication = Autodesk.AutoCAD.ApplicationServices.Application;
using CoreApplication = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(Civil3D.HybridMcp.Bridge.BridgeCommands))]

namespace Civil3D.HybridMcp.Bridge;

public sealed class BridgeApplication : IExtensionApplication
{
    public void Initialize()
    {
        BridgeStartupLogger.Info("initialize_start");
        try
        {
            BridgeRuntime.Current ??= BridgeRuntime.Start();
            BridgeStartupLogger.Info(
                "initialize_success",
                BridgeRuntime.Current?.InstanceId,
                BridgeRuntime.Current?.PipeName);
        }
        catch (System.Exception ex)
        {
            BridgeStartupLogger.Error("initialize_failure", null, null, ex);
            throw;
        }
    }

    public void Terminate()
    {
        BridgeStartupLogger.Info(
            "terminate_start",
            BridgeRuntime.Current?.InstanceId,
            BridgeRuntime.Current?.PipeName);
        try
        {
            BridgeRuntime.Current?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            BridgeRuntime.Current = null;
            BridgeStartupLogger.Info("terminate_success");
        }
        catch (System.Exception ex)
        {
            BridgeStartupLogger.Error("terminate_failure", null, null, ex);
            throw;
        }
    }
}

public sealed class BridgeCommands
{
    [CommandMethod("C3DHMCPSTATUS")]
    public void Status()
    {
        WriteStatus();
    }

    [CommandMethod("C3DHYBRIDSTATUS")]
    public void HybridStatus()
    {
        WriteStatus();
    }

    private static void WriteStatus()
    {
        var runtime = BridgeRuntime.Current;
        var editor = CoreApplication.DocumentManager.MdiActiveDocument?.Editor;
        editor?.WriteMessage(
            runtime is null
                ? "\nCivil3D Hybrid MCP bridge is not running."
                : "\n" + runtime.DescribeStatus());
    }

    [CommandMethod("C3DHMCPSTOP")]
    public void Stop()
    {
        BridgeRuntime.Current?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        BridgeRuntime.Current = null;
        CoreApplication.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nCivil3D Hybrid MCP bridge stopped.");
    }
}

public sealed class BridgeRuntime : IAsyncDisposable
{
    private readonly AutodeskIdleDispatcher _dispatcher;
    private readonly AutodeskDocumentSnapshotService _documents;
    private readonly WindowDialogMonitor _dialogMonitor;
    private readonly ToolRegistry _toolRegistry;
    private readonly EndpointRegistryWriter _endpointWriter;
    private readonly NamedPipeRpcServer _pipeServer;
    private readonly Timer _heartbeatTimer;
    private readonly object _descriptorLock = new();
    private EndpointDescriptor _descriptor;

    private BridgeRuntime(
        string instanceId,
        string pipeName,
        AutodeskIdleDispatcher dispatcher,
        AutodeskDocumentSnapshotService documents,
        WindowDialogMonitor dialogMonitor,
        ToolRegistry toolRegistry,
        EndpointRegistryWriter endpointWriter,
        NamedPipeRpcServer pipeServer,
        EndpointDescriptor descriptor)
    {
        InstanceId = instanceId;
        PipeName = pipeName;
        _dispatcher = dispatcher;
        _documents = documents;
        _dialogMonitor = dialogMonitor;
        _toolRegistry = toolRegistry;
        _endpointWriter = endpointWriter;
        _pipeServer = pipeServer;
        _descriptor = descriptor;
        _heartbeatTimer = new Timer(_ => _ = RefreshEndpointAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public static BridgeRuntime? Current { get; set; }
    public string InstanceId { get; }
    public string PipeName { get; }

    public static BridgeRuntime Start()
    {
        BridgeRuntime? runtime = null;
        NamedPipeRpcServer? pipeServer = null;

        try
        {
            var instanceId = Guid.NewGuid().ToString("N");
            var process = Process.GetCurrentProcess();
            var pipeName = $"{ProtocolConstants.PipePrefix}-{process.Id}-{instanceId}";
            BridgeStartupLogger.Info("autodesk_reference_probe", instanceId, pipeName, AutodeskRuntimeInfo.GetAssemblySnapshot());

            var dispatcher = new AutodeskIdleDispatcher();
            var documents = new AutodeskDocumentSnapshotService(instanceId);
            var dialogMonitor = new WindowDialogMonitor(new Win32WindowEnumerator());
            var tools = new BridgeToolCatalog();
            var layerService = new LayerService();
            var objectService = new ObjectService();
            var blockService = new BlockService();
            var civilService = new CivilReadService();
            var surveyService = new SurveyGeometryService(
                new BridgeSurveyDatabaseGateway(
                    new SurveyDatabaseEnvironmentGateway(AutodeskRuntimeInfo.GetCivil3DAssemblyDirectory()),
                    new AutodeskSurveyComGateway()));
            var toolRegistry = new ToolRegistry(CreateToolHandlers(
                tools,
                instanceId,
                documents,
                dispatcher,
                dialogMonitor,
                layerService,
                objectService,
                blockService,
                civilService,
                surveyService));
            var endpointWriter = new EndpointRegistryWriter(EndpointRegistryOptions.Default());

            var descriptor = CreateDescriptor(instanceId, pipeName, documents, tools, pipeListening: true, endpointRegistered: false);
            pipeServer = new NamedPipeRpcServer(pipeName, async (request, ct) =>
                await HandleRpcAsync(request, ct, instanceId, documents, dispatcher, dialogMonitor, tools, toolRegistry, () => descriptor));

            runtime = new BridgeRuntime(
                instanceId,
                pipeName,
                dispatcher,
                documents,
                dialogMonitor,
                toolRegistry,
                endpointWriter,
                pipeServer,
                descriptor);

            descriptor = runtime.UpdateDescriptor(pipeListening: true, endpointRegistered: true);
            try
            {
                pipeServer.Start();
                BridgeStartupLogger.Info("named_pipe_started", instanceId, pipeName);
            }
            catch (System.Exception ex)
            {
                BridgeStartupLogger.Error("named_pipe_start_failed", instanceId, pipeName, ex);
                throw;
            }

            try
            {
                endpointWriter.Write(descriptor);
                BridgeStartupLogger.Info("endpoint_registered", instanceId, pipeName, new JsonObject
                {
                    ["endpointDirectory"] = EndpointRegistryOptions.Default().DirectoryPath
                });
            }
            catch (System.Exception ex)
            {
                BridgeStartupLogger.Error("endpoint_write_failed", instanceId, pipeName, ex);
                throw;
            }

            Current = runtime;
            return runtime;
        }
        catch (System.Exception ex)
        {
            BridgeStartupLogger.Error("bridge_start_failed", runtime?.InstanceId, runtime?.PipeName, ex);
            if (runtime is not null)
            {
                runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            else if (pipeServer is not null)
            {
                pipeServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            throw;
        }
    }

    public string DescribeStatus()
    {
        EndpointDescriptor descriptor;
        lock (_descriptorLock)
        {
            descriptor = _descriptor;
        }

        var activeDocument = descriptor.ActiveDocument;
        var readOnly = activeDocument is null ? "unknown" : activeDocument.IsReadOnly.ToString();
        var status = _pipeServer.IsRunning ? "running" : "pipe_not_running";
        var endpointRegistered = descriptor.Capabilities.FeatureFlags.TryGetValue("endpoint_registered", out var registered) && registered;

        return "Civil3D Hybrid MCP bridge running. " +
            $"Status={status}, " +
            $"BridgeVersion={ProtocolConstants.BridgeVersion}, " +
            $"Protocol={ProtocolVersion.Current}, " +
            $"InstanceId={InstanceId}, " +
            $"Pipe={PipeName}, " +
            $"EndpointRegistered={endpointRegistered}, " +
            $"ActiveDocument={activeDocument?.Name ?? "none"}, " +
            $"ReadOnly={readOnly}";
    }

    public async ValueTask DisposeAsync()
    {
        _heartbeatTimer.Dispose();
        _endpointWriter.Remove(_descriptor);
        await _pipeServer.DisposeAsync();
        _dispatcher.Dispose();
    }

    private EndpointDescriptor UpdateDescriptor(bool pipeListening, bool endpointRegistered)
    {
        var descriptor = CreateDescriptor(InstanceId, PipeName, _documents, new BridgeToolCatalog(), pipeListening, endpointRegistered);
        lock (_descriptorLock)
        {
            _descriptor = descriptor;
        }

        return descriptor;
    }

    private async Task RefreshEndpointAsync()
    {
        try
        {
            await _dispatcher.ExecuteAsync(_ =>
            {
                var descriptor = UpdateDescriptor(pipeListening: _pipeServer.IsRunning, endpointRegistered: true);
                _endpointWriter.Write(descriptor);
                return 0;
            }, CancellationToken.None);
        }
        catch
        {
            // Heartbeat failures are visible as stale endpoints to the server.
        }
    }

    private static async Task<JsonRpcResponse> HandleRpcAsync(
        JsonRpcRequest request,
        CancellationToken cancellationToken,
        string instanceId,
        AutodeskDocumentSnapshotService documents,
        AutodeskIdleDispatcher dispatcher,
        WindowDialogMonitor dialogMonitor,
        BridgeToolCatalog catalog,
        ToolRegistry registry,
        Func<EndpointDescriptor> descriptorAccessor)
    {
        try
        {
            return request.Method switch
            {
                "bridge/handshake" => JsonRpcResponse.Success(
                    request.Id,
                    await CreateHandshakeAsync(request, dispatcher, catalog, descriptorAccessor, cancellationToken)),
                "tools/list" => JsonRpcResponse.Success(request.Id, new ToolManifest
                {
                    Version = ProtocolVersion.Current,
                    Tools = catalog.ListTools()
                }),
                "tools/execute" => JsonRpcResponse.Success(
                    request.Id,
                    await ExecuteToolAsync(request, instanceId, registry, cancellationToken)),
                _ => JsonRpcResponse.Failure(request.Id, ErrorCode.InvalidRequest, $"Unknown JSON-RPC method '{request.Method}'.")
            };
        }
        catch (System.Exception ex)
        {
            return JsonRpcResponse.Failure(request.Id, ErrorCode.InternalError, ex.Message);
        }
    }

    private static async Task<HandshakeResponse> CreateHandshakeAsync(
        JsonRpcRequest request,
        AutodeskIdleDispatcher dispatcher,
        BridgeToolCatalog catalog,
        Func<EndpointDescriptor> descriptorAccessor,
        CancellationToken cancellationToken)
    {
        var handshake = request.Params?.Deserialize<HandshakeRequest>(JsonContractOptions.Default)
            ?? throw new InvalidOperationException("Handshake request was missing.");

        var compatibility = VersionNegotiator.Evaluate(handshake.ProtocolVersion, ProtocolVersion.Current);
        var descriptor = await dispatcher.ExecuteAsync(_ => descriptorAccessor(), cancellationToken);
        var instance = ToInstanceDescriptor(descriptor);
        var capabilities = descriptor.Capabilities;

        return new HandshakeResponse
        {
            Success = compatibility.Status != CompatibilityStatus.Incompatible,
            Compatibility = compatibility.Status,
            Message = compatibility.Reason,
            Instance = instance,
            CapabilityManifest = capabilities,
            Tools = catalog.ListTools(),
            Error = compatibility.Status == CompatibilityStatus.Incompatible
                ? new ErrorEnvelope
                {
                    Code = ErrorCode.ProtocolIncompatible,
                    Message = compatibility.Reason
                }
                : null
        };
    }

    private static async Task<ToolResponse> ExecuteToolAsync(
        JsonRpcRequest request,
        string instanceId,
        ToolRegistry registry,
        CancellationToken cancellationToken)
    {
        var toolRequest = request.Params?.Deserialize<ToolRequest>(JsonContractOptions.Default)
            ?? throw new InvalidOperationException("Tool request was missing.");

        if (!string.Equals(toolRequest.InstanceId, instanceId, StringComparison.Ordinal))
        {
            return new ToolResponse
            {
                Success = false,
                Tool = toolRequest.ToolName,
                CorrelationId = toolRequest.CorrelationId,
                Error = new ErrorEnvelope
                {
                    Code = ErrorCode.InstanceNotFound,
                    Message = "Tool request instance_id does not match this bridge.",
                    CorrelationId = toolRequest.CorrelationId
                }
            };
        }

        if (!registry.TryGet(toolRequest.ToolName, out var handler))
        {
            return new ToolResponse
            {
                Success = false,
                Tool = toolRequest.ToolName,
                CorrelationId = toolRequest.CorrelationId,
                Error = new ErrorEnvelope
                {
                    Code = ErrorCode.ToolNotFound,
                    Message = $"Tool '{toolRequest.ToolName}' is not registered.",
                    CorrelationId = toolRequest.CorrelationId
                }
            };
        }

        return await handler.ExecuteAsync(toolRequest, cancellationToken);
    }

    private static EndpointDescriptor CreateDescriptor(
        string instanceId,
        string pipeName,
        AutodeskDocumentSnapshotService documents,
        BridgeToolCatalog catalog,
        bool pipeListening,
        bool endpointRegistered)
    {
        var process = Process.GetCurrentProcess();
        var productVersion = AutodeskRuntimeInfo.GetProductVersion();
        var activeDocument = documents.GetActiveDocumentOrNull();
        var capabilities = CreateCapabilityManifest(instanceId, productVersion, catalog);

        return new EndpointDescriptor
        {
            InstanceId = instanceId,
            ProcessId = process.Id,
            ProcessStartTimeUtc = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero),
            Product = "Autodesk Civil 3D",
            ProductVersion = productVersion,
            BridgeVersion = ProtocolConstants.BridgeVersion,
            ProtocolVersion = ProtocolVersion.Current,
            PipeName = pipeName,
            StartedUtc = DateTimeOffset.UtcNow,
            LastHeartbeatUtc = DateTimeOffset.UtcNow,
            ActiveDocument = activeDocument,
            Capabilities = capabilities with
            {
                FeatureFlags = new Dictionary<string, bool>(capabilities.FeatureFlags, StringComparer.OrdinalIgnoreCase)
                {
                    ["pipe_listening"] = pipeListening,
                    ["endpoint_registered"] = endpointRegistered
                }
            }
        };
    }

    private static CapabilityManifest CreateCapabilityManifest(
        string instanceId,
        string productVersion,
        BridgeToolCatalog catalog) =>
        new()
        {
            InstanceId = instanceId,
            Product = "Autodesk Civil 3D",
            ProductVersion = productVersion,
            BridgeVersion = ProtocolConstants.BridgeVersion,
            ProtocolVersion = ProtocolVersion.Current,
            ToolManifestVersion = ProtocolVersion.Current,
            ApiVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["autocad"] = AutodeskRuntimeInfo.GetAutoCadApiVersion(),
                ["civil3d"] = productVersion
            },
            Capabilities = new CapabilityFlags
            {
                Core = true,
                AutoCad = true,
                Civil3D = true,
                Survey = true,
                Diagnostics = true,
                DialogMonitor = "active",
                SurveyCom = true,
                DeveloperMode = false
            },
            FeatureFlags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["confirmation_tokens"] = true,
                ["named_pipe_current_user_only"] = true,
                ["developer_mode_enabled"] = false,
                ["survey_enabled"] = true,
                ["survey_geometry_enabled"] = true,
                ["survey_fbk_dry_run_enabled"] = true,
                ["survey_database_status_enabled"] = true,
                ["survey_working_folder_get_enabled"] = true,
                ["survey_project_list_enabled"] = true,
                ["survey_network_list_enabled"] = true
            },
            Tools = catalog.ListTools()
        };

    private static InstanceDescriptor ToInstanceDescriptor(EndpointDescriptor descriptor) =>
        new()
        {
            InstanceId = descriptor.InstanceId,
            ProcessId = descriptor.ProcessId,
            ProcessStartTimeUtc = descriptor.ProcessStartTimeUtc,
            Product = descriptor.Product,
            ProductVersion = descriptor.ProductVersion,
            BridgeVersion = descriptor.BridgeVersion,
            ProtocolVersion = descriptor.ProtocolVersion,
            PipeName = descriptor.PipeName,
            ActiveDocument = descriptor.ActiveDocument,
            DocumentCount = descriptor.ActiveDocument is null ? 0 : 1
        };

    private static IEnumerable<IToolHandler> CreateToolHandlers(
        BridgeToolCatalog catalog,
        string instanceId,
        AutodeskDocumentSnapshotService documents,
        AutodeskIdleDispatcher dispatcher,
        WindowDialogMonitor dialogMonitor,
        LayerService layerService,
        ObjectService objectService,
        BlockService blockService,
        CivilReadService civilService,
        SurveyGeometryService surveyService)
    {
        foreach (var descriptor in catalog.ListTools())
        {
            if (string.Equals(descriptor.Module, "AutoCAD", StringComparison.OrdinalIgnoreCase))
            {
                // The AutoCAD module has three handlers, so it routes by tool
                // name instead of by module alone.
                yield return descriptor.Name switch
                {
                    AutoCadToolNames.ObjectGet or AutoCadToolNames.ObjectProperties =>
                        new AutoCadObjectToolHandler(descriptor, instanceId, documents, dispatcher, objectService),
                    AutoCadToolNames.BlockList or AutoCadToolNames.BlockReferenceList or AutoCadToolNames.BlockGetAttributes =>
                        new AutoCadBlockToolHandler(descriptor, instanceId, documents, dispatcher, blockService),
                    _ => new AutoCadLayerToolHandler(descriptor, instanceId, documents, dispatcher, layerService)
                };
            }
            else if (string.Equals(descriptor.Module, "Civil3D", StringComparison.OrdinalIgnoreCase))
            {
                yield return new CivilToolHandler(descriptor, instanceId, documents, dispatcher, civilService);
            }
            else if (string.Equals(descriptor.Module, "Roadway", StringComparison.OrdinalIgnoreCase))
            {
                yield return new RoadwayToolHandler(descriptor, instanceId, documents, dispatcher);
            }
            else if (string.Equals(descriptor.Module, "Survey", StringComparison.OrdinalIgnoreCase))
            {
                yield return new SurveyToolHandler(descriptor, instanceId, surveyService, dispatcher, documents.GetActiveDocumentOrNull);
            }
            else
            {
                yield return new BridgeToolHandler(descriptor, instanceId, documents, dispatcher, dialogMonitor, catalog);
            }
        }
    }
}

public sealed class BridgeToolCatalog
{
    private readonly IReadOnlyList<ToolDescriptor> _tools = CoreTools()
        .Concat(AutoCadToolCatalog.ListTools())
        .Concat(CivilToolCatalog.ListTools())
        .Concat(SurveyToolCatalog.ListTools())
        .Concat(RoadwayToolCatalog.ListTools())
        .ToArray();

    public IReadOnlyList<ToolDescriptor> ListTools() => _tools;

    private static ToolDescriptor ReadTool(string name, string description, string module, string outputTitle) =>
        new()
        {
            Name = name,
            Description = description,
            OperationClass = OperationClass.Read,
            Risk = ToolRisk.Low,
            InputSchema = ToolSchema.EmptyInput(),
            OutputSchema = ToolSchema.ObjectOutput(outputTitle),
            Timeout = TimeSpan.FromSeconds(15),
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.NoneRequired,
            Module = module,
            Version = ProtocolVersion.Current
        };

    private static IReadOnlyList<ToolDescriptor> CoreTools() =>
    [
        ReadTool("civil3d_ping", "Verify bridge reachability and version information.", "Core", "PingResponse"),
        ReadTool("civil3d_status", "Return Civil 3D bridge, document, command, dialog, and capability status.", "Diagnostics", "StatusResponse"),
        ReadTool("drawing_info", "Return read-only identity and metadata for the active drawing.", "Core", "DrawingInfoResponse"),
        ReadTool("document_list", "List open documents in the current Civil 3D instance with stable session identities.", "Core", "DocumentListResponse"),
        ReadTool("list_dialogs", "List host windows and report whether a modal dialog is blocking the Civil 3D thread. Answers without using the Autodesk host context so it still works while Civil 3D is blocked.", "Diagnostics", "DialogListResponse"),
        ReadTool("dialog_decision_list", "List the operator's stored decisions for known dialogs, plus any dialog on screen that still needs one, with the options to choose from.", "Diagnostics", "DialogDecisionListResponse"),
        DecisionTool("dialog_decision_set", "Record the operator's decision for one dialog: which button to press, and whether that answer is the default from now on or good only once.", "DialogDecisionSetResponse"),
        DismissTool("dismiss_dialog", "Close one dialog by pressing the button the operator decided on. Requires expected_title, and presses nothing when no decision applies.", "DialogDismissResponse")
    ];

    /// <summary>
    /// The only tool that acts on the host UI. Medium risk because the caller
    /// chooses the button and the platform cannot know what that button does.
    /// </summary>
    private static ToolDescriptor DismissTool(string name, string description, string outputTitle) =>
        new()
        {
            Name = name,
            Description = description,
            OperationClass = OperationClass.Write,
            Risk = ToolRisk.Medium,
            InputSchema = ToolSchema.ObjectOutput(name + "Input"),
            OutputSchema = ToolSchema.ObjectOutput(outputTitle),
            Timeout = TimeSpan.FromSeconds(15),
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.NotSupported,
            Module = "Diagnostics",
            Version = ProtocolVersion.Current
        };

    /// <summary>
    /// Writes operator configuration, never the drawing, so it carries no
    /// rollback semantics and never touches a transaction.
    /// </summary>
    private static ToolDescriptor DecisionTool(string name, string description, string outputTitle) =>
        new()
        {
            Name = name,
            Description = description,
            OperationClass = OperationClass.Write,
            Risk = ToolRisk.Low,
            InputSchema = ToolSchema.ObjectOutput(name + "Input"),
            OutputSchema = ToolSchema.ObjectOutput(outputTitle),
            Timeout = TimeSpan.FromSeconds(15),
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.NoneRequired,
            Module = "Diagnostics",
            Version = ProtocolVersion.Current
        };
}

public sealed class BridgeToolHandler : IToolHandler
{
    private readonly ToolDescriptor _descriptor;
    private readonly string _instanceId;
    private readonly AutodeskDocumentSnapshotService _documents;
    private readonly AutodeskIdleDispatcher _dispatcher;
    private readonly WindowDialogMonitor _dialogMonitor;
    private readonly BridgeToolCatalog _catalog;

    public BridgeToolHandler(
        ToolDescriptor descriptor,
        string instanceId,
        AutodeskDocumentSnapshotService documents,
        AutodeskIdleDispatcher dispatcher,
        WindowDialogMonitor dialogMonitor,
        BridgeToolCatalog catalog)
    {
        _descriptor = descriptor;
        _instanceId = instanceId;
        _documents = documents;
        _dispatcher = dispatcher;
        _dialogMonitor = dialogMonitor;
        _catalog = catalog;
    }

    public ToolDescriptor Descriptor => _descriptor;

    public async Task<ToolResponse> ExecuteAsync(ToolRequest request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            // list_dialogs must never touch the Autodesk host context. It is the
            // tool an operator calls when that context is already blocked by a
            // modal, so dispatching it would hang exactly when it is needed.
            var result = IsHostFreeTool(request.ToolName)
                ? ExecuteWithoutAutodeskContext(request)
                : await _dispatcher.ExecuteAsync(ct => ExecuteOnAutodeskContext(request, ct), cancellationToken);
            return new ToolResponse
            {
                Success = true,
                Tool = request.ToolName,
                CorrelationId = request.CorrelationId,
                Document = result.Document,
                Data = JsonContractOptions.ToJsonNode(result.Data),
                Duration = Stopwatch.GetElapsedTime(started)
            };
        }
        catch (ToolExecutionException ex)
        {
            return Error(request, ex.Code, ex.Message, started, ex.Details);
        }
        catch (DialogToolException ex)
        {
            return Error(request, ex.Code, ex.Message, started);
        }
        catch (JsonException ex)
        {
            return Error(request, ErrorCode.InvalidRequest, ex.Message, started);
        }
        catch (System.Exception ex)
        {
            return Error(request, ErrorCode.InternalError, ex.Message, started);
        }
    }

    // These answer from Win32 window state and operator configuration alone.
    // They must stay off the Autodesk dispatcher: they exist for the case where
    // that dispatcher is blocked by the very dialog being decided.
    private static bool IsHostFreeTool(string toolName) =>
        string.Equals(toolName, "list_dialogs", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(toolName, "dialog_decision_list", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(toolName, "dialog_decision_set", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(toolName, "dismiss_dialog", StringComparison.OrdinalIgnoreCase);

    private CoreToolExecutionResult ExecuteWithoutAutodeskContext(ToolRequest request)
    {
        object data = request.ToolName.ToLowerInvariant() switch
        {
            "list_dialogs" => _dialogMonitor.List(_instanceId),
            "dialog_decision_list" => _dialogMonitor.ListDecisions(_instanceId),
            "dialog_decision_set" => _dialogMonitor.SetDecision(ReadDecisionRequest(request)),
            "dismiss_dialog" => _dialogMonitor.Dismiss(ReadDismissRequest(request), new Win32DialogActuator()),
            _ => throw new ToolExecutionException(ErrorCode.ToolNotFound, $"Unknown host-free tool '{request.ToolName}'.")
        };

        return new CoreToolExecutionResult(null, data);
    }

    private DialogDecisionSetRequest ReadDecisionRequest(ToolRequest request)
    {
        if (request.Arguments is null)
        {
            throw new ToolExecutionException(ErrorCode.InvalidRequest, "dialog_decision_set requires structured arguments.");
        }

        var input = request.Arguments.Deserialize<DialogDecisionSetRequest>(JsonContractOptions.Default)
            ?? throw new ToolExecutionException(ErrorCode.InvalidRequest, "dialog_decision_set arguments could not be read.");

        if (string.IsNullOrWhiteSpace(input.DialogTitle) || string.IsNullOrWhiteSpace(input.ButtonText))
        {
            throw new ToolExecutionException(
                ErrorCode.InvalidRequest,
                "dialog_decision_set requires dialog_title and button_text; a decision names one dialog and one button.");
        }

        return input with { InstanceId = _instanceId };
    }

    private DialogDismissRequest ReadDismissRequest(ToolRequest request)
    {
        if (request.Arguments is null)
        {
            throw new ToolExecutionException(ErrorCode.InvalidRequest, "dismiss_dialog requires structured arguments.");
        }

        var input = request.Arguments.Deserialize<DialogDismissRequest>(JsonContractOptions.Default)
            ?? throw new ToolExecutionException(ErrorCode.InvalidRequest, "dismiss_dialog arguments could not be read.");

        return input with { InstanceId = _instanceId };
    }

    private CoreToolExecutionResult ExecuteOnAutodeskContext(ToolRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        object data = request.ToolName.ToLowerInvariant() switch
        {
            "civil3d_ping" => new PingResponse
            {
                InstanceId = _instanceId,
                BridgeVersion = ProtocolConstants.BridgeVersion,
                ProtocolVersion = ProtocolVersion.Current,
                ProductVersion = AutodeskRuntimeInfo.GetProductVersion()
            },
            "civil3d_status" => BuildStatus(),
            "document_list" => new DocumentListResponse
            {
                InstanceId = _instanceId,
                Documents = _documents.ListDocuments()
            },
            "drawing_info" => new DrawingInfoResponse
            {
                Document = _documents.GetActiveDocumentOrThrow()
            },
            _ => throw new ToolExecutionException(ErrorCode.ToolNotFound, $"Unknown tool '{request.ToolName}'.")
        };

        return new CoreToolExecutionResult(_documents.GetActiveDocumentOrNull(), data);
    }

    private static ToolResponse Error(ToolRequest request, ErrorCode code, string message, long started, JsonObject? details = null) =>
        new()
        {
            Success = false,
            Tool = request.ToolName,
            CorrelationId = request.CorrelationId,
            Error = new ErrorEnvelope
            {
                Code = code,
                Message = message,
                CorrelationId = request.CorrelationId,
                Details = details
            },
            Duration = Stopwatch.GetElapsedTime(started)
        };

    private StatusResponse BuildStatus()
    {
        var documents = _documents.ListDocuments();
        var active = documents.FirstOrDefault(document => document.IsActive);
        var productVersion = AutodeskRuntimeInfo.GetProductVersion();
        var instance = new InstanceDescriptor
        {
            InstanceId = _instanceId,
            ProcessId = Environment.ProcessId,
            ProcessStartTimeUtc = new DateTimeOffset(Process.GetCurrentProcess().StartTime.ToUniversalTime(), TimeSpan.Zero),
            Product = "Autodesk Civil 3D",
            ProductVersion = productVersion,
            BridgeVersion = ProtocolConstants.BridgeVersion,
            ProtocolVersion = ProtocolVersion.Current,
            PipeName = $"{ProtocolConstants.PipePrefix}-{Environment.ProcessId}-{_instanceId}",
            ActiveDocument = active,
            DocumentCount = documents.Count
        };

        return new StatusResponse
        {
            Civil3DRunning = true,
            Instance = instance,
            DocumentCount = documents.Count,
            ActiveDocument = active,
            ActiveCommand = AutodeskRuntimeInfo.GetActiveCommand(),
            DialogState = _dialogMonitor.GetStatus(),
            Capabilities = new CapabilityManifest
            {
                InstanceId = _instanceId,
                Product = instance.Product,
                ProductVersion = productVersion,
                BridgeVersion = ProtocolConstants.BridgeVersion,
                ProtocolVersion = ProtocolVersion.Current,
                ToolManifestVersion = ProtocolVersion.Current,
                Capabilities = new CapabilityFlags
                {
                    Core = true,
                    AutoCad = true,
                    Civil3D = true,
                    Survey = true,
                    Diagnostics = true,
                    DialogMonitor = "active",
                    SurveyCom = true,
                    DeveloperMode = false
                },
                FeatureFlags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
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
                Tools = _catalog.ListTools()
            },
            BridgeHealth = new BridgeHealth
            {
                PipeListening = true,
                EndpointRegistered = true
            }
        };
    }

    private sealed record CoreToolExecutionResult(DocumentDescriptor? Document, object Data);
}

public sealed class AutodeskDocumentSnapshotService : IAutoCadDocumentResolver, ICivilDocumentResolver
{
    private readonly string _instanceId;

    public AutodeskDocumentSnapshotService(string instanceId)
    {
        _instanceId = instanceId;
    }

    public IReadOnlyList<DocumentDescriptor> ListDocuments()
    {
        var result = new List<DocumentDescriptor>();
        var manager = CoreApplication.DocumentManager;
        var active = manager.MdiActiveDocument;
        foreach (Document document in manager)
        {
            result.Add(CreateDescriptor(document, ReferenceEquals(document, active)));
        }

        return result;
    }

    public DocumentDescriptor? GetActiveDocumentOrNull()
    {
        var active = CoreApplication.DocumentManager.MdiActiveDocument;
        return active is null ? null : CreateDescriptor(active, isActive: true);
    }

    public DocumentDescriptor GetActiveDocumentOrThrow() =>
        GetActiveDocumentOrNull()
        ?? throw new ToolExecutionException(
            ErrorCode.DocumentNotFound,
            "No active drawing is open.",
            new JsonObject
            {
                ["document_count"] = CountDocuments(),
                ["requires_active_document"] = true
            });

    public ResolvedAutoCadDocument Resolve(string? documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            throw new LayerToolException(ErrorCode.DocumentAmbiguous, "document_id is required for layer tools.");
        }

        var matches = new List<ResolvedAutoCadDocument>();
        var manager = CoreApplication.DocumentManager;
        var active = manager.MdiActiveDocument;
        foreach (Document document in manager)
        {
            var descriptor = CreateDescriptor(document, ReferenceEquals(document, active));
            if (string.Equals(descriptor.DocumentId, documentId, StringComparison.Ordinal))
            {
                matches.Add(new ResolvedAutoCadDocument(document, descriptor));
            }
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new LayerToolException(ErrorCode.DocumentNotFound, "Target drawing document was not found in this Civil 3D instance."),
            _ => throw new LayerToolException(ErrorCode.DocumentAmbiguous, "Multiple open drawings matched the requested document_id.")
        };
    }

    public ResolvedCivilDocument ResolveCivilDocument(string? documentId)
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            throw new CivilToolException(ErrorCode.DocumentAmbiguous, "document_id is required for Civil 3D tools.");
        }

        var matches = new List<ResolvedCivilDocument>();
        var manager = CoreApplication.DocumentManager;
        var active = manager.MdiActiveDocument;
        foreach (Document document in manager)
        {
            var descriptor = CreateDescriptor(document, ReferenceEquals(document, active));
            if (string.Equals(descriptor.DocumentId, documentId, StringComparison.Ordinal))
            {
                matches.Add(new ResolvedCivilDocument(document, descriptor));
            }
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new CivilToolException(ErrorCode.DocumentNotFound, "Target drawing document was not found in this Civil 3D instance."),
            _ => throw new CivilToolException(ErrorCode.DocumentAmbiguous, "Multiple open drawings matched the requested document_id.")
        };
    }

    private DocumentDescriptor CreateDescriptor(Document document, bool isActive)
    {
        var database = document.Database;
        var fullPath = GetFullPath(document, database);
        var name = string.IsNullOrWhiteSpace(fullPath) ? Path.GetFileName(document.Name) : Path.GetFileName(fullPath);
        var sessionIdentifier = RuntimeHelpers.GetHashCode(document).ToString("X");

        return new DocumentDescriptor
        {
            InstanceId = _instanceId,
            DocumentId = DocumentIdentity.ComputeDocumentId(_instanceId, name, fullPath, sessionIdentifier),
            Name = name,
            FullPath = fullPath,
            IsActive = isActive,
            IsSaved = !string.IsNullOrWhiteSpace(database.Filename) && File.Exists(database.Filename),
            IsReadOnly = document.IsReadOnly,
            Units = Safe(() => database.Insunits.ToString()),
            DatabaseFingerprint = Safe(() => database.FingerprintGuid.ToString()),
            SessionIdentifier = sessionIdentifier
        };
    }

    private static string? GetFullPath(Document document, Database database)
    {
        var path = !string.IsNullOrWhiteSpace(database.Filename) ? database.Filename : document.Name;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.IsPathFullyQualified(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    private static T? Safe<T>(Func<T> getter)
    {
        try { return getter(); }
        catch { return default; }
    }

    private static int CountDocuments()
    {
        try
        {
            var count = 0;
            foreach (Document _ in CoreApplication.DocumentManager)
            {
                count++;
            }

            return count;
        }
        catch
        {
            return 0;
        }
    }
}

public sealed class AutodeskIdleDispatcher : IHostApplicationDispatcher, IDisposable
{
    private readonly Queue<Func<Task>> _queue = new();
    private readonly object _gate = new();
    private bool _subscribed;

    public Task<T> ExecuteAsync<T>(Func<CancellationToken, T> action, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_gate)
        {
            _queue.Enqueue(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    completion.TrySetResult(action(cancellationToken));
                }
                catch (System.Exception ex)
                {
                    completion.TrySetException(ex);
                }

                return Task.CompletedTask;
            });

            if (!_subscribed)
            {
                AcadApplication.Idle += OnIdle;
                _subscribed = true;
            }
        }

        return completion.Task.WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_subscribed)
            {
                AcadApplication.Idle -= OnIdle;
                _subscribed = false;
            }

            _queue.Clear();
        }
    }

    private void OnIdle(object? sender, EventArgs e)
    {
        while (true)
        {
            Func<Task>? work;
            lock (_gate)
            {
                if (_queue.Count == 0)
                {
                    AcadApplication.Idle -= OnIdle;
                    _subscribed = false;
                    return;
                }

                work = _queue.Dequeue();
            }

            work().GetAwaiter().GetResult();
        }
    }
}

public sealed class BridgeSurveyDatabaseGateway : ISurveyDatabaseGateway
{
    private readonly ISurveyDatabaseGateway _statusGateway;
    private readonly ISurveyDatabaseGateway _workingFolderGateway;

    public BridgeSurveyDatabaseGateway(
        ISurveyDatabaseGateway statusGateway,
        ISurveyDatabaseGateway workingFolderGateway)
    {
        _statusGateway = statusGateway;
        _workingFolderGateway = workingFolderGateway;
    }

    public SurveyDatabaseStatusResponse GetStatus(string instanceId) =>
        _statusGateway.GetStatus(instanceId);

    public SurveyWorkingFolderGetResponse GetWorkingFolder(string instanceId) =>
        _workingFolderGateway.GetWorkingFolder(instanceId);

    public SurveyProjectListResponse ListProjects(string instanceId) =>
        _workingFolderGateway.ListProjects(instanceId);

    public SurveyNetworkListResponse ListNetworks(string instanceId, string? projectName) =>
        _workingFolderGateway.ListNetworks(instanceId, projectName);
}

public sealed class AutodeskSurveyComGateway : ISurveyDatabaseGateway
{
    private static readonly string[] SurveyDocumentProgIds =
    [
        "AeccXUiSurvey.AeccSurveyDocument.13.8",
        "AeccXUiSurvey.AeccSurveyDocument.13.7",
        "AeccXUiSurvey.AeccSurveyDocument.13.6",
        "AeccXUiSurvey.AeccSurveyDocument.13.5"
    ];

    private static readonly string[] CivilApplicationProgIds =
    [
        "AeccXUiLand.AeccApplication.13.8",
        "AeccXUiLand.AeccApplication.13.7",
        "AeccXUiLand.AeccApplication.13.6",
        "AeccXUiLand.AeccApplication.13.5"
    ];

    private object? _cachedSurveyDocument;
    private long _cachedDocumentKey;

    public SurveyDatabaseStatusResponse GetStatus(string instanceId) =>
        new()
        {
            InstanceId = instanceId,
            SurveyComAvailable = true,
            SurveyDatabaseAvailable = false,
            RequiresActiveDocument = true,
            Adapter = "survey_com",
            Status = "adapter_available",
            InteropAssemblies =
            [
                "Autodesk.AECC.Interop.Survey.dll"
            ],
            Capabilities =
            [
                "working_folder_read",
                "project_list_read",
                "network_list_read"
            ],
            Limitations =
            [
                "Survey COM adapter is limited to read-only working-folder, project-list, and network-list access in this phase.",
                "Survey project open, setup, observation, traverse, figure, and import operations are not enabled yet."
            ]
        };

    public SurveyWorkingFolderGetResponse GetWorkingFolder(string instanceId)
    {
        var manager = CoreApplication.DocumentManager;
        var activeDocument = manager.MdiActiveDocument;
        if (activeDocument is null)
        {
            throw new SurveyToolException(
                ErrorCode.DocumentNotFound,
                "Survey working folder read requires an active Civil 3D document.",
                new JsonObject
                {
                    ["adapter"] = "survey_com",
                    ["document_count"] = CountDocuments(manager),
                    ["requires_active_document"] = true
                });
        }

        var projects = GetProjects(activeDocument);

        string? workingFolder;
        try
        {
            workingFolder = projects.GetWorkingFolder();
        }
        catch (System.Exception ex)
        {
            throw new SurveyToolException(
                ErrorCode.NotSupported,
                $"Survey COM GetWorkingFolder failed: {RootMessage(ex)}");
        }

        return new SurveyWorkingFolderGetResponse
        {
            InstanceId = instanceId,
            Adapter = "survey_com",
            Status = "working_folder_available",
            WorkingFolder = string.IsNullOrWhiteSpace(workingFolder) ? null : workingFolder,
            RequiresActiveDocument = true,
            Capabilities =
            [
                "working_folder_read"
            ],
            Limitations =
            [
                "Read-only Survey COM access; no Survey Database was opened or modified.",
                "Project, network, setup, observation, figure, and import operations are not enabled yet."
            ]
        };
    }

    public SurveyProjectListResponse ListProjects(string instanceId)
    {
        var manager = CoreApplication.DocumentManager;
        var activeDocument = manager.MdiActiveDocument;
        if (activeDocument is null)
        {
            throw new SurveyToolException(
                ErrorCode.DocumentNotFound,
                "Survey project listing requires an active Civil 3D document.",
                new JsonObject
                {
                    ["adapter"] = "survey_com",
                    ["document_count"] = CountDocuments(manager),
                    ["requires_active_document"] = true
                });
        }

        var projects = GetProjects(activeDocument);
        string? workingFolder;
        try
        {
            workingFolder = projects.GetWorkingFolder();
        }
        catch (System.Exception ex)
        {
            throw new SurveyToolException(
                ErrorCode.NotSupported,
                $"Survey COM GetWorkingFolder failed before project listing: {RootMessage(ex)}");
        }

        var projectInfos = new List<SurveyProjectInfo>();
        int count;
        try
        {
            count = projects.Count;
        }
        catch (System.Exception ex)
        {
            throw new SurveyToolException(
                ErrorCode.NotSupported,
                $"Survey COM Projects.Count failed: {RootMessage(ex)}");
        }

        for (var i = 0; i < count; i++)
        {
            Autodesk.AECC.Interop.Survey.IAeccSurveyProject project;
            try
            {
                project = projects[i];
            }
            catch (System.Exception ex)
            {
                throw new SurveyToolException(
                    ErrorCode.NotSupported,
                    $"Survey COM project index {i} could not be read: {RootMessage(ex)}");
            }

            string name;
            try
            {
                name = project.Name;
            }
            catch (System.Exception ex)
            {
                throw new SurveyToolException(
                    ErrorCode.NotSupported,
                    $"Survey COM project index {i} name could not be read: {RootMessage(ex)}");
            }

            projectInfos.Add(new SurveyProjectInfo
            {
                Name = name,
                Path = SafeProjectString(() => project.Path),
                ProjectGuid = SafeProjectString(() => project.ProjectGUID)
            });
        }

        return new SurveyProjectListResponse
        {
            InstanceId = instanceId,
            Adapter = "survey_com",
            Status = "projects_available",
            WorkingFolder = string.IsNullOrWhiteSpace(workingFolder) ? null : workingFolder,
            Count = projectInfos.Count,
            Projects = projectInfos,
            RequiresActiveDocument = true,
            Capabilities =
            [
                "working_folder_read",
                "project_list_read"
            ],
            Limitations =
            [
                "Read-only Survey COM access; no Survey Database was opened or modified.",
                "Network, setup, observation, figure, and import operations are not enabled yet."
            ]
        };
    }

    public SurveyNetworkListResponse ListNetworks(string instanceId, string? projectName)
    {
        var manager = CoreApplication.DocumentManager;
        var activeDocument = manager.MdiActiveDocument;
        if (activeDocument is null)
        {
            throw new SurveyToolException(
                ErrorCode.DocumentNotFound,
                "Survey network listing requires an active Civil 3D document.",
                new JsonObject
                {
                    ["adapter"] = "survey_com",
                    ["document_count"] = CountDocuments(manager),
                    ["requires_active_document"] = true
                });
        }

        var projects = GetProjects(activeDocument);
        string? workingFolder;
        try
        {
            workingFolder = projects.GetWorkingFolder();
        }
        catch (System.Exception ex)
        {
            throw new SurveyToolException(
                ErrorCode.NotSupported,
                $"Survey COM GetWorkingFolder failed before network listing: {RootMessage(ex)}");
        }

        int projectCount;
        try
        {
            projectCount = projects.Count;
        }
        catch (System.Exception ex)
        {
            throw new SurveyToolException(
                ErrorCode.NotSupported,
                $"Survey COM Projects.Count failed before network listing: {RootMessage(ex)}");
        }

        var matchedProjects = new List<(Autodesk.AECC.Interop.Survey.IAeccSurveyProject Project, string Name)>();
        for (var i = 0; i < projectCount; i++)
        {
            Autodesk.AECC.Interop.Survey.IAeccSurveyProject project;
            try
            {
                project = projects[i];
            }
            catch (System.Exception ex)
            {
                throw new SurveyToolException(
                    ErrorCode.NotSupported,
                    $"Survey COM project index {i} could not be read: {RootMessage(ex)}");
            }

            string name;
            try
            {
                name = project.Name;
            }
            catch (System.Exception ex)
            {
                throw new SurveyToolException(
                    ErrorCode.NotSupported,
                    $"Survey COM project index {i} name could not be read: {RootMessage(ex)}");
            }

            if (projectName is null || string.Equals(name, projectName, StringComparison.OrdinalIgnoreCase))
            {
                matchedProjects.Add((project, name));
            }
        }

        if (projectName is not null && matchedProjects.Count == 0)
        {
            throw new SurveyToolException(
                ErrorCode.SurveyProjectNotFound,
                $"Survey project '{projectName}' was not found in the current Survey working folder.",
                new JsonObject
                {
                    ["adapter"] = "survey_com",
                    ["project_count"] = projectCount,
                    ["working_folder"] = workingFolder
                });
        }

        var networkInfos = new List<SurveyNetworkInfo>();
        var warnings = new List<string>();

        foreach (var (project, projectDisplayName) in matchedProjects)
        {
            Autodesk.AECC.Interop.Survey.IAeccSurveyNetworks networks;
            try
            {
                networks = project.Networks;
            }
            catch (System.Exception ex)
            {
                if (projectName is not null)
                {
                    throw new SurveyToolException(
                        ErrorCode.NotSupported,
                        $"Survey COM networks collection for project '{projectDisplayName}' could not be read without opening the Survey project: {RootMessage(ex)}");
                }

                warnings.Add($"Survey project '{projectDisplayName}' networks could not be read: {RootMessage(ex)}");
                continue;
            }

            int networkCount;
            try
            {
                networkCount = networks.Count;
            }
            catch (System.Exception ex)
            {
                if (projectName is not null)
                {
                    throw new SurveyToolException(
                        ErrorCode.NotSupported,
                        $"Survey COM Networks.Count failed for project '{projectDisplayName}': {RootMessage(ex)}");
                }

                warnings.Add($"Survey project '{projectDisplayName}' network count could not be read: {RootMessage(ex)}");
                continue;
            }

            for (var i = 0; i < networkCount; i++)
            {
                Autodesk.AECC.Interop.Survey.IAeccSurveyNetwork network;
                try
                {
                    network = networks[i];
                }
                catch (System.Exception ex)
                {
                    warnings.Add($"Survey project '{projectDisplayName}' network index {i} could not be read: {RootMessage(ex)}");
                    continue;
                }

                string networkName;
                try
                {
                    networkName = network.Name;
                }
                catch (System.Exception ex)
                {
                    warnings.Add($"Survey project '{projectDisplayName}' network index {i} name could not be read: {RootMessage(ex)}");
                    continue;
                }

                networkInfos.Add(new SurveyNetworkInfo
                {
                    Name = networkName,
                    ProjectName = projectDisplayName,
                    Description = SafeProjectString(() => network.Description),
                    Path = SafeProjectString(() => network.Path),
                    Id = SafeProjectInt(() => network.ID)
                });
            }
        }

        var status = projectCount == 0
            ? "no_projects"
            : warnings.Count > 0
                ? "networks_partial"
                : "networks_available";

        return new SurveyNetworkListResponse
        {
            InstanceId = instanceId,
            Adapter = "survey_com",
            Status = status,
            ProjectName = projectName,
            WorkingFolder = string.IsNullOrWhiteSpace(workingFolder) ? null : workingFolder,
            Count = networkInfos.Count,
            Networks = networkInfos,
            RequiresActiveDocument = true,
            Capabilities =
            [
                "working_folder_read",
                "project_list_read",
                "network_list_read"
            ],
            Limitations =
            [
                "Read-only Survey COM access; no Survey Database or Survey project was opened or modified.",
                "Network metadata only; setup, observation, traverse, figure, and import operations are not enabled yet."
            ],
            Warnings = warnings
        };
    }

    private Autodesk.AECC.Interop.Survey.IAeccSurveyProjects GetProjects(Document activeDocument)
    {
        object surveyDocument = GetSurveyDocument(activeDocument);
        object projectsObject;
        try
        {
            projectsObject = GetComProperty(surveyDocument, "Projects");
        }
        catch (System.Exception ex)
        {
            throw new SurveyToolException(
                ErrorCode.NotSupported,
                $"Survey COM document was resolved, but its Projects collection could not be read: {RootMessage(ex)}");
        }

        if (projectsObject is not Autodesk.AECC.Interop.Survey.IAeccSurveyProjects projects)
        {
            throw new SurveyToolException(
                ErrorCode.NotSupported,
                $"Survey COM Projects collection was not castable to IAeccSurveyProjects. Runtime type: {projectsObject.GetType().FullName}.");
        }

        return projects;
    }

    private object GetSurveyDocument(Document activeDocument)
    {
        var documentKey = activeDocument.Database.UnmanagedObject.ToInt64();
        if (_cachedSurveyDocument is not null && _cachedDocumentKey == documentKey)
        {
            return _cachedSurveyDocument;
        }

        object? acadApplication = AcadApplication.AcadApplication;
        if (acadApplication is null)
        {
            throw new SurveyToolException(
                ErrorCode.NotSupported,
                "Application.AcadApplication is null; Civil 3D COM is not ready.");
        }

        var tried = new List<string>();
        object? surveyDocument = ResolveComObject(acadApplication, SurveyDocumentProgIds, tried);
        if (surveyDocument is null)
        {
            object? civilApplication = ResolveComObject(acadApplication, CivilApplicationProgIds, tried);
            if (civilApplication is not null)
            {
                surveyDocument = ResolveComObject(civilApplication, SurveyDocumentProgIds, tried);
            }
        }

        if (surveyDocument is null)
        {
            throw new SurveyToolException(
                ErrorCode.NotSupported,
                "Could not resolve the Civil 3D Survey COM document. Tried: " + string.Join("; ", tried));
        }

        try
        {
            object? activeComDocument = GetComProperty(acadApplication, "ActiveDocument");
            if (activeComDocument is not null)
            {
                InvokeCom(surveyDocument, "Init", activeComDocument);
            }
        }
        catch
        {
            // Some Survey COM objects are already initialized. Property access below confirms readiness.
        }

        _cachedSurveyDocument = surveyDocument;
        _cachedDocumentKey = documentKey;
        return surveyDocument;
    }

    private static object? ResolveComObject(object host, IEnumerable<string> progIds, ICollection<string> tried)
    {
        foreach (var progId in progIds)
        {
            try
            {
                var resolved = InvokeCom(host, "GetInterfaceObject", progId);
                if (resolved is not null)
                {
                    tried.Add($"{progId}:OK");
                    return resolved;
                }

                tried.Add($"{progId}:null");
            }
            catch (System.Exception ex)
            {
                tried.Add($"{progId}:{RootMessage(ex)}");
            }
        }

        return null;
    }

    private static object? InvokeCom(object target, string method, params object?[] args) =>
        target.GetType().InvokeMember(
            method,
            BindingFlags.InvokeMethod,
            binder: null,
            target,
            args,
            CultureInfo.InvariantCulture);

    private static object GetComProperty(object target, string property) =>
        target.GetType().InvokeMember(
            property,
            BindingFlags.GetProperty,
            binder: null,
            target,
            args: null,
            CultureInfo.InvariantCulture)
        ?? throw new InvalidOperationException($"COM property '{property}' returned null.");

    private static string RootMessage(System.Exception ex)
    {
        var current = ex;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return $"{current.GetType().Name}: {current.Message}";
    }

    private static string? SafeProjectString(Func<string?> read)
    {
        try
        {
            var value = read();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static int? SafeProjectInt(Func<int> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }

    private static int CountDocuments(DocumentCollection manager)
    {
        try
        {
            var count = 0;
            foreach (Document _ in manager)
            {
                count++;
            }

            return count;
        }
        catch
        {
            return 0;
        }
    }
}

public static class AutodeskRuntimeInfo
{
    public static string GetProductVersion()
    {
        try { return CoreApplication.Version.ToString(); }
        catch { return "Unknown"; }
    }

    public static string GetAutoCadApiVersion()
    {
        try { return CoreApplication.GetSystemVariable("ACADVER")?.ToString() ?? "Unknown"; }
        catch { return "Unknown"; }
    }

    public static string? GetActiveCommand()
    {
        try
        {
            var value = CoreApplication.GetSystemVariable("CMDNAMES")?.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    public static JsonObject GetAssemblySnapshot()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies();
        var snapshot = new JsonObject
        {
            ["targetFramework"] = "net8.0-windows",
            ["autodeskReferencePolicy"] = "Private=false; Autodesk DLLs are resolved from the installed AutoCAD/Civil 3D host.",
            ["productVersion"] = GetProductVersion(),
            ["autoCadApiVersion"] = GetAutoCadApiVersion()
        };

        foreach (var name in new[] { "acmgd", "acdbmgd", "accoremgd", "AeccDbMgd" })
        {
            var assembly = loaded.FirstOrDefault(candidate =>
                string.Equals(candidate.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
            snapshot[name] = assembly is null
                ? "not_loaded"
                : assembly.GetName().Version?.ToString() ?? "unknown";
        }

        return snapshot;
    }

    public static string? GetCivil3DAssemblyDirectory()
    {
        var civil3DDirectory = GetLoadedAssemblyDirectory("AeccDbMgd");
        if (!string.IsNullOrWhiteSpace(civil3DDirectory))
        {
            return civil3DDirectory;
        }

        var autoCadDirectory =
            GetAssemblyDirectory(typeof(CoreApplication).Assembly) ??
            GetLoadedAssemblyDirectory("acmgd") ??
            GetLoadedAssemblyDirectory("acdbmgd") ??
            GetLoadedAssemblyDirectory("accoremgd");
        if (string.IsNullOrWhiteSpace(autoCadDirectory))
        {
            return null;
        }

        var c3dDirectory = Path.Combine(autoCadDirectory, "C3D");
        return Directory.Exists(c3dDirectory) ? c3dDirectory : autoCadDirectory;
    }

    private static string? GetLoadedAssemblyDirectory(string assemblyName)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate =>
            string.Equals(candidate.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
        return GetAssemblyDirectory(assembly);
    }

    private static string? GetAssemblyDirectory(System.Reflection.Assembly? assembly)
    {
        if (assembly is null || string.IsNullOrWhiteSpace(assembly.Location))
        {
            return null;
        }

        try { return Path.GetDirectoryName(assembly.Location); }
        catch { return null; }
    }
}

public static class BridgeStartupLogger
{
    public static void Info(
        string eventName,
        string? instanceId = null,
        string? pipeName = null,
        JsonObject? details = null) =>
        Write("info", eventName, instanceId, pipeName, details, null);

    public static void Error(
        string eventName,
        string? instanceId,
        string? pipeName,
        System.Exception exception,
        JsonObject? details = null) =>
        Write("error", eventName, instanceId, pipeName, details, exception);

    private static void Write(
        string level,
        string eventName,
        string? instanceId,
        string? pipeName,
        JsonObject? details,
        System.Exception? exception)
    {
        try
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ProtocolConstants.EndpointFolderName,
                ProtocolConstants.LogSubfolderName);
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, $"bridge-{DateTimeOffset.UtcNow:yyyyMMdd}.jsonl");
            var entry = new JsonObject
            {
                ["timestampUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["component"] = "bridge",
                ["level"] = level,
                ["event"] = eventName,
                ["instanceId"] = instanceId,
                ["pipeName"] = pipeName,
                ["bridgeVersion"] = ProtocolConstants.BridgeVersion,
                ["protocolVersion"] = ProtocolVersion.Current.ToString(),
                ["details"] = details
            };

            if (exception is not null)
            {
                entry["exception"] = new JsonObject
                {
                    ["type"] = exception.GetType().FullName,
                    ["message"] = exception.Message,
                    ["hResult"] = exception.HResult
                };
            }

            File.AppendAllText(path, entry.ToJsonString(JsonContractOptions.Default) + Environment.NewLine);
        }
        catch
        {
            // Logging must never prevent Civil 3D from loading or unloading the bridge.
        }
    }
}
