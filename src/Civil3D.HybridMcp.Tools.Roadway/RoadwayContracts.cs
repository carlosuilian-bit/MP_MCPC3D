using System.Text.Json.Serialization;

namespace Civil3D.HybridMcp.Protocol;

/// <summary>
/// Station and offset for a list of points against one alignment.
///
/// The roadway module sits on top of the generic Civil 3D tools rather than
/// duplicating them: the per-point geometry is the same `alignment_station_offset`
/// already validated live. What this adds is the batch, and the discipline a
/// batch needs.
/// </summary>
public sealed record RoadwayStationOffsetBatchRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    /// <summary>Alignment name, matching how `alignment_list` reports it.</summary>
    public required string Alignment { get; init; }

    public IReadOnlyList<RoadwayPointInput> Points { get; init; } = Array.Empty<RoadwayPointInput>();
}

public sealed record RoadwayPointInput
{
    public required double X { get; init; }

    public required double Y { get; init; }

    /// <summary>Caller's own label for the point, echoed back untouched.</summary>
    public string? Label { get; init; }
}

public sealed record RoadwayStationOffsetBatchResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public required string Alignment { get; init; }

    public int Count { get; init; }

    /// <summary>Points that produced a station and offset.</summary>
    [JsonPropertyName("resolved_count")]
    public int ResolvedCount { get; init; }

    /// <summary>
    /// Points the alignment could not answer for. A non-zero value is a normal
    /// outcome, not an error: a point off the end of the alignment is ordinary
    /// in roadway work.
    /// </summary>
    [JsonPropertyName("failed_count")]
    public int FailedCount { get; init; }

    public IReadOnlyList<RoadwayStationOffsetPoint> Points { get; init; } = Array.Empty<RoadwayStationOffsetPoint>();
}

public sealed record RoadwayStationOffsetPoint
{
    /// <summary>Position in the request, so a caller can match rows without relying on order.</summary>
    public required int Index { get; init; }

    public required double X { get; init; }

    public required double Y { get; init; }

    public string? Label { get; init; }

    public required bool Resolved { get; init; }

    public double? Station { get; init; }

    public double? Offset { get; init; }

    /// <summary>Why this point has no station, when it has none.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Updates a property set across many objects in one atomic write.
///
/// Takes explicit handles, never a filter. The caller runs `propertyset_find`,
/// looks at the result, and passes the list, so the platform can never expand a
/// query into a write nobody saw.
/// </summary>
public sealed record RoadwayPropertySetWriteRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("set_name")]
    public required string SetName { get; init; }

    public IReadOnlyList<RoadwayPropertySetTarget> Objects { get; init; } = Array.Empty<RoadwayPropertySetTarget>();
}

public sealed record RoadwayPropertySetTarget
{
    public required string Handle { get; init; }

    public IReadOnlyList<PropertySetValueUpdate> Properties { get; init; } = Array.Empty<PropertySetValueUpdate>();
}

public sealed record RoadwayPropertySetWriteResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("set_name")]
    public required string SetName { get; init; }

    public required bool Applied { get; init; }

    [JsonPropertyName("object_count")]
    public int ObjectCount { get; init; }

    [JsonPropertyName("changed_count")]
    public int ChangedCount { get; init; }

    /// <summary>Objects already holding the requested values, so nothing was written to them.</summary>
    [JsonPropertyName("skipped_count")]
    public int SkippedCount { get; init; }

    public IReadOnlyList<RoadwayPropertySetResult> Objects { get; init; } = Array.Empty<RoadwayPropertySetResult>();

    public WriteTransactionInfo? Transaction { get; init; }
}

public sealed record RoadwayPropertySetResult
{
    public required string Handle { get; init; }

    public required bool Changed { get; init; }

    public IReadOnlyList<PropertySetValueChange> Properties { get; init; } = Array.Empty<PropertySetValueChange>();
}
