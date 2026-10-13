using System.Text.Json.Serialization;

namespace Civil3D.HybridMcp.Protocol;

public sealed record LayerListRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("name_contains")]
    public string? NameContains { get; init; }

    [JsonPropertyName("is_on")]
    public bool? IsOn { get; init; }

    [JsonPropertyName("is_frozen")]
    public bool? IsFrozen { get; init; }

    [JsonPropertyName("is_locked")]
    public bool? IsLocked { get; init; }

    [JsonPropertyName("is_plottable")]
    public bool? IsPlottable { get; init; }
}

public sealed record LayerGetRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public string? Name { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }
}

public sealed record LayerCreateRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public required string Name { get; init; }

    [JsonPropertyName("color_index")]
    public short? ColorIndex { get; init; }

    [JsonPropertyName("is_plottable")]
    public bool? IsPlottable { get; init; }

    public string? Description { get; init; }
}

public sealed record LayerSetCurrentRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public string? Name { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }
}

public sealed record LayerUpdateRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public string? Name { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }

    [JsonPropertyName("new_name")]
    public string? NewName { get; init; }

    [JsonPropertyName("color_index")]
    public short? ColorIndex { get; init; }

    [JsonPropertyName("is_on")]
    public bool? IsOn { get; init; }

    [JsonPropertyName("is_frozen")]
    public bool? IsFrozen { get; init; }

    [JsonPropertyName("is_locked")]
    public bool? IsLocked { get; init; }

    [JsonPropertyName("is_plottable")]
    public bool? IsPlottable { get; init; }

    public string? Description { get; init; }
}

public sealed record LayerListResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public int Count { get; init; }

    public IReadOnlyList<LayerInfo> Layers { get; init; } = Array.Empty<LayerInfo>();
}

public sealed record LayerGetResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public required LayerInfo Layer { get; init; }
}

public sealed record LayerCreateResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public bool Created { get; init; }

    public required LayerInfo Layer { get; init; }

    public WriteTransactionInfo Transaction { get; init; } = new();
}

public sealed record LayerSetCurrentResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public bool Changed { get; init; }

    [JsonPropertyName("previous_layer")]
    public required LayerInfo PreviousLayer { get; init; }

    [JsonPropertyName("current_layer")]
    public required LayerInfo CurrentLayer { get; init; }

    public WriteTransactionInfo Transaction { get; init; } = new();
}

public sealed record LayerUpdateResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public bool Changed { get; init; }

    [JsonPropertyName("previous_layer")]
    public required LayerInfo PreviousLayer { get; init; }

    [JsonPropertyName("updated_layer")]
    public required LayerInfo UpdatedLayer { get; init; }

    public WriteTransactionInfo Transaction { get; init; } = new();
}

public sealed record WriteTransactionInfo
{
    [JsonPropertyName("document_locked")]
    public bool DocumentLocked { get; init; }

    [JsonPropertyName("transaction_started")]
    public bool TransactionStarted { get; init; }

    public bool Committed { get; init; }

    [JsonPropertyName("rolled_back")]
    public bool RolledBack { get; init; }
}

public sealed record LayerInfo
{
    public required string Name { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }

    [JsonPropertyName("is_current")]
    public bool IsCurrent { get; init; }

    [JsonPropertyName("is_on")]
    public bool IsOn { get; init; }

    [JsonPropertyName("is_frozen")]
    public bool IsFrozen { get; init; }

    [JsonPropertyName("is_locked")]
    public bool IsLocked { get; init; }

    [JsonPropertyName("is_plottable")]
    public bool IsPlottable { get; init; }

    public LayerColorInfo? Color { get; init; }

    public string? Linetype { get; init; }

    public string? Lineweight { get; init; }

    public string? Description { get; init; }
}

public sealed record LayerColorInfo
{
    [JsonPropertyName("color_index")]
    public short? ColorIndex { get; init; }

    public string? Method { get; init; }
}

public sealed record CadObjectReference
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("database_fingerprint")]
    public string? DatabaseFingerprint { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_id")]
    public string? ObjectId { get; init; }

    public string? Type { get; init; }

    public string? Name { get; init; }

    [JsonIgnore]
    public bool HasLayerLocator => !string.IsNullOrWhiteSpace(Name) || !string.IsNullOrWhiteSpace(Handle);
}
