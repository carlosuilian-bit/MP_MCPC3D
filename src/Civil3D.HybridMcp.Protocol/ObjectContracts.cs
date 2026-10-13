using System.Text.Json.Serialization;

namespace Civil3D.HybridMcp.Protocol;

/// <summary>
/// Generic AutoCAD object reads addressed by handle.
///
/// Not to be confused with <see cref="CivilObjectGetRequest"/>, which resolves a
/// named Civil 3D object such as an alignment or a surface. These contracts
/// describe any <c>DBObject</c> in the drawing, including Civil 3D and AEC types,
/// and are what the caller falls back to when no typed tool exists yet.
/// </summary>
public sealed record ObjectGetRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }
}

public sealed record ObjectGetResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public required CadObjectInfo Object { get; init; }
}

public sealed record ObjectPropertiesRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }

    [JsonPropertyName("name_contains")]
    public string? NameContains { get; init; }

    /// <summary>
    /// Include properties whose value could not be read. Off by default so a
    /// normal request returns usable data, on when the caller is diagnosing why
    /// a property is missing.
    /// </summary>
    [JsonPropertyName("include_unreadable")]
    public bool? IncludeUnreadable { get; init; }
}

public sealed record ObjectPropertiesResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public required string Handle { get; init; }

    [JsonPropertyName("object_class")]
    public string? ObjectClass { get; init; }

    public int Count { get; init; }

    /// <summary>
    /// True when the adapter stopped before enumerating every property, so the
    /// caller knows the list is a prefix and not the whole object.
    /// </summary>
    public bool Truncated { get; init; }

    public IReadOnlyList<ObjectPropertyInfo> Properties { get; init; } = Array.Empty<ObjectPropertyInfo>();
}

public sealed record CadObjectInfo
{
    public required string Handle { get; init; }

    [JsonPropertyName("object_id")]
    public string? ObjectId { get; init; }

    /// <summary>Runtime class name, for example <c>AeccDbAlignment</c>.</summary>
    [JsonPropertyName("object_class")]
    public required string ObjectClass { get; init; }

    [JsonPropertyName("dxf_name")]
    public string? DxfName { get; init; }

    [JsonPropertyName("app_name")]
    public string? AppName { get; init; }

    /// <summary>Parent classes from the immediate base upward.</summary>
    [JsonPropertyName("class_hierarchy")]
    public IReadOnlyList<string> ClassHierarchy { get; init; } = Array.Empty<string>();

    [JsonPropertyName("is_entity")]
    public bool IsEntity { get; init; }

    [JsonPropertyName("is_erased")]
    public bool IsErased { get; init; }

    [JsonPropertyName("is_proxy")]
    public bool IsProxy { get; init; }

    [JsonPropertyName("is_persistent")]
    public bool IsPersistent { get; init; }

    [JsonPropertyName("owner_handle")]
    public string? OwnerHandle { get; init; }

    [JsonPropertyName("owner_class")]
    public string? OwnerClass { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }

    /// <summary>Entity-level data, or null when the object is not an entity.</summary>
    public CadEntityInfo? Entity { get; init; }
}

public sealed record CadEntityInfo
{
    public string? Layer { get; init; }

    public string? Linetype { get; init; }

    [JsonPropertyName("linetype_scale")]
    public double? LinetypeScale { get; init; }

    public string? Lineweight { get; init; }

    [JsonPropertyName("color_index")]
    public int? ColorIndex { get; init; }

    public string? Color { get; init; }

    /// <summary>Alpha channel as AutoCAD reports it: 255 is fully opaque.</summary>
    [JsonPropertyName("transparency_alpha")]
    public int? TransparencyAlpha { get; init; }

    [JsonPropertyName("is_visible")]
    public bool? IsVisible { get; init; }

    [JsonPropertyName("block_name")]
    public string? BlockName { get; init; }

    /// <summary>Geometric extents, or null when the entity refused to report them.</summary>
    public CadExtentsInfo? Extents { get; init; }
}

public sealed record CadExtentsInfo
{
    [JsonPropertyName("min_x")]
    public double MinX { get; init; }

    [JsonPropertyName("min_y")]
    public double MinY { get; init; }

    [JsonPropertyName("min_z")]
    public double MinZ { get; init; }

    [JsonPropertyName("max_x")]
    public double MaxX { get; init; }

    [JsonPropertyName("max_y")]
    public double MaxY { get; init; }

    [JsonPropertyName("max_z")]
    public double MaxZ { get; init; }
}

/// <summary>
/// One property read off the object. Mirrors the Phase 2F property-set shape on
/// purpose: an invariant <see cref="Value"/> string plus a separate
/// <see cref="NumericValue"/>, so a caller computing stations or widths never has
/// to reparse localized text.
/// </summary>
public sealed record ObjectPropertyInfo
{
    public required string Name { get; init; }

    /// <summary>Type that declares the property, so callers can tell an entity-level property from a Civil 3D one.</summary>
    [JsonPropertyName("declaring_type")]
    public string? DeclaringType { get; init; }

    [JsonPropertyName("data_type")]
    public string? DataType { get; init; }

    public string? Value { get; init; }

    [JsonPropertyName("numeric_value")]
    public double? NumericValue { get; init; }

    [JsonPropertyName("is_read_only")]
    public bool? IsReadOnly { get; init; }

    /// <summary>
    /// Why the value is missing, when the host refused to evaluate the property.
    /// A failed read is reported here rather than turned into a fake value.
    /// </summary>
    public string? Error { get; init; }
}
