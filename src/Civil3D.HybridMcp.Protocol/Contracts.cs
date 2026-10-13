using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Civil3D.HybridMcp.Protocol;

public static class ProtocolConstants
{
    public const string ProductName = "Civil3D Hybrid MCP";
    public const string ServerName = "Civil3D.HybridMcp.Server";
    public const string BridgeName = "Civil3D.HybridMcp.Bridge";
    public const string PipePrefix = "Civil3DHybridMcp";
    public const string EndpointFolderName = "Civil3DHybridMcp";
    public const string EndpointSubfolderName = "endpoints";
    public const string LogSubfolderName = "logs";
    public const string BridgeVersion = "0.1.0";
    public const string ServerVersion = "0.1.0";
    public const string ToolManifestVersion = "1.0.0";
}

public enum OperationClass
{
    Read,
    Write,
    Destructive
}

public enum ToolRisk
{
    Low,
    Medium,
    High,
    Critical
}

public enum RollbackSemantics
{
    NoneRequired,
    TransactionRollbackBeforeCommit,
    BestEffort,
    NotSupported
}

public enum CompatibilityStatus
{
    Compatible,
    PartiallyCompatible,
    Incompatible
}

public enum EndpointHealth
{
    Unknown,
    Healthy,
    StaleHeartbeat,
    ProcessExited,
    ProcessStartMismatch,
    InvalidDescriptor,
    PipeUnavailable
}

public enum ErrorCode
{
    InstanceNotFound,
    InstanceAmbiguous,
    BridgeUnavailable,
    ProtocolIncompatible,
    DocumentNotFound,
    DocumentAmbiguous,
    BlockedByDialog,
    Timeout,
    Cancelled,
    InvalidRequest,
    InternalError,
    NotSupported,
    ToolNotFound,
    ToolIncompatible,
    ConfirmationRequired,
    ConfirmationInvalid,
    ConfirmationExpired,
    LayerNotFound,
    LayerAlreadyExists,
    InvalidLayerName,
    LayerCreateFailed,
    DocumentLockFailed,
    TransactionFailed,
    RollbackFailed,
    AlignmentNotFound,
    SurfaceNotFound,
    CogoPointNotFound,
    CivilQueryFailed,
    SurveyProjectNotFound,
    ProfileNotFound,
    ProfileAmbiguous,
    CorridorNotFound,
    PropertySetNotFound,
    ObjectNotFound,
    DialogNotFound,
    DialogAmbiguous,
    DialogDismissFailed,
    PropertyNotFound
}

public sealed record ErrorEnvelope
{
    public required ErrorCode Code { get; init; }
    public required string Message { get; init; }
    public string? CorrelationId { get; init; }
    public JsonObject? Details { get; init; }
}

public sealed record SuccessEnvelope
{
    public bool Success { get; init; } = true;
    public string? Message { get; init; }
    public JsonNode? Data { get; init; }
    public TimeSpan? Duration { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed record HandshakeRequest
{
    public required string ServerName { get; init; }
    public required string ServerVersion { get; init; }
    public required ProtocolVersion ProtocolVersion { get; init; }
    public required string SessionId { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record HandshakeResponse
{
    public bool Success { get; init; }
    public CompatibilityStatus Compatibility { get; init; }
    public string? Message { get; init; }
    public InstanceDescriptor? Instance { get; init; }
    public CapabilityManifest? CapabilityManifest { get; init; }
    public IReadOnlyList<ToolDescriptor> Tools { get; init; } = Array.Empty<ToolDescriptor>();
    public ErrorEnvelope? Error { get; init; }
}

public sealed record EndpointDescriptor
{
    public required string InstanceId { get; init; }
    public required int ProcessId { get; init; }
    public required DateTimeOffset ProcessStartTimeUtc { get; init; }
    public required string Product { get; init; }
    public required string ProductVersion { get; init; }
    public required string BridgeVersion { get; init; }
    public required ProtocolVersion ProtocolVersion { get; init; }
    public required string PipeName { get; init; }
    public required DateTimeOffset StartedUtc { get; init; }
    public required DateTimeOffset LastHeartbeatUtc { get; init; }
    public DocumentDescriptor? ActiveDocument { get; init; }
    public CapabilityManifest Capabilities { get; init; } = new();
}

public sealed record InstanceDescriptor
{
    public required string InstanceId { get; init; }
    public required int ProcessId { get; init; }
    public required DateTimeOffset ProcessStartTimeUtc { get; init; }
    public required string Product { get; init; }
    public required string ProductVersion { get; init; }
    public required string BridgeVersion { get; init; }
    public required ProtocolVersion ProtocolVersion { get; init; }
    public required string PipeName { get; init; }
    public DocumentDescriptor? ActiveDocument { get; init; }
    public int DocumentCount { get; init; }
}

public sealed record DocumentDescriptor
{
    public required string InstanceId { get; init; }
    public required string DocumentId { get; init; }
    public required string Name { get; init; }
    public string? FullPath { get; init; }
    public bool IsActive { get; init; }
    public bool IsSaved { get; init; }
    public bool IsReadOnly { get; init; }
    public string? Units { get; init; }
    public string? DatabaseFingerprint { get; init; }
    public required string SessionIdentifier { get; init; }
}

public sealed record CapabilityManifest
{
    public string InstanceId { get; init; } = "";
    public string Product { get; init; } = "Unknown";
    public string ProductVersion { get; init; } = "Unknown";
    public string BridgeVersion { get; init; } = ProtocolConstants.BridgeVersion;
    public ProtocolVersion ProtocolVersion { get; init; } = ProtocolVersion.Current;
    public ProtocolVersion ToolManifestVersion { get; init; } = ProtocolVersion.Current;
    public IDictionary<string, string> ApiVersions { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public CapabilityFlags Capabilities { get; init; } = new();
    public IDictionary<string, bool> FeatureFlags { get; init; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<ToolDescriptor> Tools { get; init; } = Array.Empty<ToolDescriptor>();
}

public sealed record CapabilityFlags
{
    public bool Core { get; init; }
    public bool AutoCad { get; init; }
    public bool Civil3D { get; init; }
    public bool Diagnostics { get; init; }
    public string DialogMonitor { get; init; } = "none";
    public bool Survey { get; init; }
    public bool SurveyCom { get; init; }
    public bool DeveloperMode { get; init; }
}

public sealed record ToolManifest
{
    public ProtocolVersion Version { get; init; } = ProtocolVersion.Current;
    public IReadOnlyList<ToolDescriptor> Tools { get; init; } = Array.Empty<ToolDescriptor>();
}

public sealed record ToolDescriptor
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required OperationClass OperationClass { get; init; }
    public required ToolRisk Risk { get; init; }
    public required JsonObject InputSchema { get; init; }
    public required JsonObject OutputSchema { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public bool RequiresConfirmation { get; init; }
    public RollbackSemantics RollbackSemantics { get; init; } = RollbackSemantics.NoneRequired;
    public required string Module { get; init; }
    public ProtocolVersion Version { get; init; } = ProtocolVersion.Current;
}

public sealed record ToolRequest
{
    public required string CorrelationId { get; init; }
    public required string SessionId { get; init; }
    public required string InstanceId { get; init; }
    public string? DocumentId { get; init; }
    public required string ToolName { get; init; }
    public JsonNode? Arguments { get; init; }
    public ConfirmationToken? ConfirmationToken { get; init; }
}

public sealed record ToolResponse
{
    public bool Success { get; init; }
    public required string Tool { get; init; }
    public required string CorrelationId { get; init; }
    public DocumentDescriptor? Document { get; init; }
    public JsonNode? Data { get; init; }
    public ErrorEnvelope? Error { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public TimeSpan? Duration { get; init; }
}

public sealed record PingRequest
{
    public string? CorrelationId { get; init; }
}

public sealed record PingResponse
{
    public bool Success { get; init; } = true;
    public required string InstanceId { get; init; }
    public required string BridgeVersion { get; init; }
    public required ProtocolVersion ProtocolVersion { get; init; }
    public required string ProductVersion { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record StatusRequest
{
    public bool IncludeCapabilities { get; init; } = true;
}

public sealed record StatusResponse
{
    public bool Civil3DRunning { get; init; }
    public required InstanceDescriptor Instance { get; init; }
    public int DocumentCount { get; init; }
    public DocumentDescriptor? ActiveDocument { get; init; }
    public string? ActiveCommand { get; init; }
    public DialogStatus DialogState { get; init; } = new();
    public CapabilityManifest? Capabilities { get; init; }
    public BridgeHealth BridgeHealth { get; init; } = new();
}

public sealed record DialogStatus
{
    public bool Supported { get; init; }
    public string State { get; init; } = "not_implemented";
    public bool HasBlockingDialog { get; init; }
    public int DialogCount { get; init; }
    public string? BlockingDialogTitle { get; init; }
    public string? Note { get; init; }
}

public sealed record DialogListRequest;

public sealed record DialogListResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("process_id")]
    public required int ProcessId { get; init; }

    [JsonPropertyName("main_window_handle")]
    public string? MainWindowHandle { get; init; }

    [JsonPropertyName("main_window_title")]
    public string? MainWindowTitle { get; init; }

    [JsonPropertyName("main_window_enabled")]
    public bool? MainWindowEnabled { get; init; }

    [JsonPropertyName("blocked_by_modal")]
    public required bool BlockedByModal { get; init; }

    [JsonPropertyName("blocking_dialog_title")]
    public string? BlockingDialogTitle { get; init; }

    public required int Count { get; init; }

    public IReadOnlyList<DialogWindowInfo> Dialogs { get; init; } = Array.Empty<DialogWindowInfo>();

    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
}

public sealed record DialogWindowInfo
{
    public required string Handle { get; init; }

    public string? Title { get; init; }

    [JsonPropertyName("class_name")]
    public string? ClassName { get; init; }

    [JsonPropertyName("is_visible")]
    public required bool IsVisible { get; init; }

    [JsonPropertyName("is_enabled")]
    public required bool IsEnabled { get; init; }

    [JsonPropertyName("is_modal_candidate")]
    public required bool IsModalCandidate { get; init; }

    /// <summary>
    /// Message text of the dialog, joined from its static labels, or null when
    /// there is none to read.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Buttons the dialog offers, in the order Windows reports them. Empty when
    /// the dialog has none or when <see cref="ContentRead"/> is false.
    /// </summary>
    public IReadOnlyList<DialogButtonInfo> Buttons { get; init; } = Array.Empty<DialogButtonInfo>();

    /// <summary>
    /// Whether the dialog's controls could be read. False means unknown, not
    /// empty: a dialog whose children could not be enumerated must never look
    /// like a dialog with no buttons.
    /// </summary>
    [JsonPropertyName("content_read")]
    public bool ContentRead { get; init; }
}

public sealed record DialogButtonInfo
{
    public required string Text { get; init; }

    [JsonPropertyName("control_id")]
    public int ControlId { get; init; }

    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; init; }
}

public sealed record BridgeHealth
{
    public bool PipeListening { get; init; }
    public bool EndpointRegistered { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record DocumentListResponse
{
    public required string InstanceId { get; init; }
    public IReadOnlyList<DocumentDescriptor> Documents { get; init; } = Array.Empty<DocumentDescriptor>();
}

public sealed record DrawingInfoResponse
{
    public required DocumentDescriptor Document { get; init; }
}

public sealed record ConfirmationRequest
{
    public required string ToolName { get; init; }
    public required string InstanceId { get; init; }
    public required string DocumentId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ArgumentsHash { get; init; }
    public required int AffectedCount { get; init; }
    public required OperationClass OperationClass { get; init; }
    public required DateTimeOffset ExpiresUtc { get; init; }
    public string? PreviewSummary { get; init; }
}

public sealed record ConfirmationToken
{
    public required string TokenId { get; init; }
    public required string ToolName { get; init; }
    public required string InstanceId { get; init; }
    public required string DocumentId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ArgumentsHash { get; init; }
    public required int AffectedCount { get; init; }
    public required OperationClass OperationClass { get; init; }
    public required DateTimeOffset IssuedUtc { get; init; }
    public required DateTimeOffset ExpiresUtc { get; init; }
    public required string Signature { get; init; }
}

public sealed record ConfirmationValidationResult
{
    public bool Success { get; init; }
    public ErrorEnvelope? Error { get; init; }

    public static ConfirmationValidationResult Valid() => new() { Success = true };

    public static ConfirmationValidationResult Invalid(ErrorCode code, string message) =>
        new()
        {
            Success = false,
            Error = new ErrorEnvelope { Code = code, Message = message }
        };
}
