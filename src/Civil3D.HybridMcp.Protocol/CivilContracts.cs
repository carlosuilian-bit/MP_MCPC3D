using System.Text.Json.Serialization;

namespace Civil3D.HybridMcp.Protocol;

public sealed record CivilObjectListRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("name_contains")]
    public string? NameContains { get; init; }
}

public sealed record CivilObjectGetRequest
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

public sealed record AlignmentStationOffsetRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public string? Name { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }

    public required double X { get; init; }

    public required double Y { get; init; }
}

public sealed record AlignmentPointLocationRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public string? Name { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }

    public required double Station { get; init; }

    public double Offset { get; init; }
}

public sealed record SurfaceElevationAtXyRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public string? Name { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }

    public required double X { get; init; }

    public required double Y { get; init; }
}

public sealed record CogoListRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("raw_description_contains")]
    public string? RawDescriptionContains { get; init; }

    [JsonPropertyName("full_description_contains")]
    public string? FullDescriptionContains { get; init; }

    public int? Limit { get; init; }
}

public sealed record CogoGetRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("point_number")]
    public long? PointNumber { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }
}

public sealed record PropertySetDefinitionListRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("name_contains")]
    public string? NameContains { get; init; }
}

public sealed record PropertySetListRequest
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
}

public sealed record PropertySetGetRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }

    public required string Name { get; init; }
}

public sealed record PropertySetNumericTestCreateRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("definition_name")]
    public required string DefinitionName { get; init; }

    [JsonPropertyName("property_name")]
    public required string PropertyName { get; init; }

    [JsonPropertyName("numeric_value")]
    public required double NumericValue { get; init; }

    [JsonPropertyName("target_kind")]
    public string? TargetKind { get; init; }
}

public sealed record PropertySetDefinitionListResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public int Count { get; init; }

    public IReadOnlyList<PropertySetDefinitionInfo> Definitions { get; init; } = Array.Empty<PropertySetDefinitionInfo>();
}

public sealed record PropertySetListResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("object_handle")]
    public required string ObjectHandle { get; init; }

    public int Count { get; init; }

    [JsonPropertyName("property_sets")]
    public IReadOnlyList<PropertySetInfo> PropertySets { get; init; } = Array.Empty<PropertySetInfo>();
}

public sealed record PropertySetGetResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("object_handle")]
    public required string ObjectHandle { get; init; }

    [JsonPropertyName("property_set")]
    public required PropertySetInfo PropertySet { get; init; }
}

public sealed record PropertySetNumericTestCreateResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("definition_name")]
    public required string DefinitionName { get; init; }

    [JsonPropertyName("definition_handle")]
    public string? DefinitionHandle { get; init; }

    [JsonPropertyName("object_handle")]
    public required string ObjectHandle { get; init; }

    [JsonPropertyName("target_kind")]
    public required string TargetKind { get; init; }

    [JsonPropertyName("property_set")]
    public required PropertySetInfo PropertySet { get; init; }

    public WriteTransactionInfo Transaction { get; init; } = new();
}

public sealed record PropertySetDefinitionInfo
{
    public required string Name { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("is_style_based")]
    public bool? IsStyleBased { get; init; }

    [JsonPropertyName("is_visible")]
    public bool? IsVisible { get; init; }

    [JsonPropertyName("is_writeable")]
    public bool? IsWriteable { get; init; }

    [JsonPropertyName("applies_to_all")]
    public bool? AppliesToAll { get; init; }

    [JsonPropertyName("applies_to")]
    public IReadOnlyList<string> AppliesTo { get; init; } = Array.Empty<string>();

    [JsonPropertyName("property_count")]
    public int? PropertyCount { get; init; }

    public IReadOnlyList<PropertyDefinitionInfo> Properties { get; init; } = Array.Empty<PropertyDefinitionInfo>();
}

public sealed record PropertyDefinitionInfo
{
    public required string Name { get; init; }

    public int? Id { get; init; }

    [JsonPropertyName("data_type")]
    public string? DataType { get; init; }

    [JsonPropertyName("is_read_only")]
    public bool? IsReadOnly { get; init; }

    [JsonPropertyName("is_visible")]
    public bool? IsVisible { get; init; }

    [JsonPropertyName("is_automatic")]
    public bool? IsAutomatic { get; init; }

    public string? Units { get; init; }

    [JsonPropertyName("default_value")]
    public string? DefaultValue { get; init; }
}

public sealed record PropertySetInfo
{
    public required string Name { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("definition_handle")]
    public string? DefinitionHandle { get; init; }

    [JsonPropertyName("property_count")]
    public int? PropertyCount { get; init; }

    public IReadOnlyList<PropertyValueInfo> Properties { get; init; } = Array.Empty<PropertyValueInfo>();
}

public sealed record PropertyValueInfo
{
    public required string Name { get; init; }

    public int? Id { get; init; }

    [JsonPropertyName("data_type")]
    public string? DataType { get; init; }

    public string? Value { get; init; }

    [JsonPropertyName("numeric_value")]
    public double? NumericValue { get; init; }

    public string? Units { get; init; }

    [JsonPropertyName("is_read_only")]
    public bool? IsReadOnly { get; init; }
}

public sealed record CorridorListResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public int Count { get; init; }

    public IReadOnlyList<CorridorInfo> Corridors { get; init; } = Array.Empty<CorridorInfo>();
}

public sealed record CorridorGetResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public required CorridorInfo Corridor { get; init; }
}

public sealed record CorridorInfo
{
    public required string Name { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }

    public string? Description { get; init; }

    [JsonPropertyName("is_out_of_date")]
    public bool? IsOutOfDate { get; init; }

    [JsonPropertyName("rebuild_automatic")]
    public bool? RebuildAutomatic { get; init; }

    [JsonPropertyName("code_set_style_name")]
    public string? CodeSetStyleName { get; init; }

    [JsonPropertyName("maximum_triangle_side_length")]
    public double? MaximumTriangleSideLength { get; init; }

    [JsonPropertyName("region_lock_mode")]
    public string? RegionLockMode { get; init; }

    [JsonPropertyName("baseline_count")]
    public int? BaselineCount { get; init; }

    [JsonPropertyName("corridor_surface_count")]
    public int? CorridorSurfaceCount { get; init; }

    public IReadOnlyList<CorridorBaselineInfo> Baselines { get; init; } = Array.Empty<CorridorBaselineInfo>();
}

public sealed record CorridorBaselineInfo
{
    public required string Name { get; init; }

    [JsonPropertyName("baseline_type")]
    public string? BaselineType { get; init; }

    [JsonPropertyName("alignment_name")]
    public string? AlignmentName { get; init; }

    [JsonPropertyName("alignment_handle")]
    public string? AlignmentHandle { get; init; }

    [JsonPropertyName("profile_name")]
    public string? ProfileName { get; init; }

    [JsonPropertyName("profile_handle")]
    public string? ProfileHandle { get; init; }

    [JsonPropertyName("start_station")]
    public double? StartStation { get; init; }

    [JsonPropertyName("end_station")]
    public double? EndStation { get; init; }

    [JsonPropertyName("region_count")]
    public int? RegionCount { get; init; }

    public IReadOnlyList<CorridorRegionInfo> Regions { get; init; } = Array.Empty<CorridorRegionInfo>();
}

public sealed record CorridorRegionInfo
{
    public required string Name { get; init; }

    [JsonPropertyName("start_station")]
    public double? StartStation { get; init; }

    [JsonPropertyName("end_station")]
    public double? EndStation { get; init; }

    [JsonPropertyName("assembly_name")]
    public string? AssemblyName { get; init; }

    [JsonPropertyName("assembly_handle")]
    public string? AssemblyHandle { get; init; }
}

public sealed record ProfileListRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("alignment_name")]
    public string? AlignmentName { get; init; }

    [JsonPropertyName("name_contains")]
    public string? NameContains { get; init; }
}

public sealed record ProfileGetRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public string? Name { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }

    [JsonPropertyName("alignment_name")]
    public string? AlignmentName { get; init; }
}

public sealed record ProfileElevationAtStationRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public string? Name { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }

    [JsonPropertyName("alignment_name")]
    public string? AlignmentName { get; init; }

    public required double Station { get; init; }
}

public sealed record ProfileListResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public int Count { get; init; }

    public IReadOnlyList<ProfileInfo> Profiles { get; init; } = Array.Empty<ProfileInfo>();
}

public sealed record ProfileGetResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public required ProfileInfo Profile { get; init; }
}

public sealed record ProfileElevationAtStationResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public required ProfileInfo Profile { get; init; }

    public required double Station { get; init; }

    public double? Elevation { get; init; }

    public double? Grade { get; init; }
}

public sealed record ProfileInfo
{
    public required string Name { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }

    public string? Description { get; init; }

    [JsonPropertyName("alignment_name")]
    public string? AlignmentName { get; init; }

    [JsonPropertyName("alignment_handle")]
    public string? AlignmentHandle { get; init; }

    [JsonPropertyName("profile_type")]
    public string? ProfileType { get; init; }

    [JsonPropertyName("starting_station")]
    public double? StartingStation { get; init; }

    [JsonPropertyName("ending_station")]
    public double? EndingStation { get; init; }

    public double? Length { get; init; }

    public double? Offset { get; init; }

    [JsonPropertyName("elevation_min")]
    public double? ElevationMin { get; init; }

    [JsonPropertyName("elevation_max")]
    public double? ElevationMax { get; init; }

    [JsonPropertyName("pvi_count")]
    public int? PviCount { get; init; }

    [JsonPropertyName("style_name")]
    public string? StyleName { get; init; }
}

public sealed record AlignmentListResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public int Count { get; init; }

    public IReadOnlyList<AlignmentInfo> Alignments { get; init; } = Array.Empty<AlignmentInfo>();
}

public sealed record AlignmentGetResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public required AlignmentInfo Alignment { get; init; }
}

public sealed record AlignmentStationOffsetResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public required AlignmentInfo Alignment { get; init; }

    public required double X { get; init; }

    public required double Y { get; init; }

    public required double Station { get; init; }

    public required double Offset { get; init; }
}

public sealed record AlignmentPointLocationResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public required AlignmentInfo Alignment { get; init; }

    public required double Station { get; init; }

    public required double Offset { get; init; }

    public required double X { get; init; }

    public required double Y { get; init; }
}

public sealed record AlignmentInfo
{
    public required string Name { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }

    public string? Description { get; init; }

    public string? Type { get; init; }

    public double? Length { get; init; }

    [JsonPropertyName("starting_station")]
    public double? StartingStation { get; init; }

    [JsonPropertyName("ending_station")]
    public double? EndingStation { get; init; }

    [JsonPropertyName("site_handle")]
    public string? SiteHandle { get; init; }

    [JsonPropertyName("style_handle")]
    public string? StyleHandle { get; init; }
}

public sealed record SurfaceListResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public int Count { get; init; }

    public IReadOnlyList<SurfaceInfo> Surfaces { get; init; } = Array.Empty<SurfaceInfo>();
}

public sealed record SurfaceGetResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public required SurfaceInfo Surface { get; init; }
}

public sealed record SurfaceElevationAtXyResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public required SurfaceInfo Surface { get; init; }

    public required double X { get; init; }

    public required double Y { get; init; }

    public required double Elevation { get; init; }
}

public sealed record SurfaceInfo
{
    public required string Name { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }

    public string? Description { get; init; }

    public string? Type { get; init; }

    [JsonPropertyName("point_count")]
    public int? PointCount { get; init; }

    [JsonPropertyName("minimum_elevation")]
    public double? MinimumElevation { get; init; }

    [JsonPropertyName("maximum_elevation")]
    public double? MaximumElevation { get; init; }

    [JsonPropertyName("mean_elevation")]
    public double? MeanElevation { get; init; }
}

public sealed record CogoListResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public int Count { get; init; }

    public IReadOnlyList<CogoPointInfo> Points { get; init; } = Array.Empty<CogoPointInfo>();
}

public sealed record CogoGetResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public required CogoPointInfo Point { get; init; }
}

public sealed record CogoPointInfo
{
    [JsonPropertyName("point_number")]
    public required long PointNumber { get; init; }

    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }

    public required double Easting { get; init; }

    public required double Northing { get; init; }

    public required double Elevation { get; init; }

    [JsonPropertyName("raw_description")]
    public string? RawDescription { get; init; }

    [JsonPropertyName("full_description")]
    public string? FullDescription { get; init; }

    [JsonPropertyName("is_locked")]
    public bool? IsLocked { get; init; }
}

/// <summary>
/// Updates values inside one AEC property set already attached to one object.
///
/// Deliberately narrow. It does not attach a set, does not create a property,
/// and does not span objects: each of those is a larger decision than changing a
/// value an engineer already put there.
/// </summary>
public sealed record PropertySetUpdateRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    /// <summary>Handle of the object carrying the property set.</summary>
    public required string Handle { get; init; }

    [JsonPropertyName("set_name")]
    public required string SetName { get; init; }

    public IReadOnlyList<PropertySetValueUpdate> Properties { get; init; } = Array.Empty<PropertySetValueUpdate>();
}

public sealed record PropertySetValueUpdate
{
    public required string Name { get; init; }

    /// <summary>Invariant text, matching how the read tools report values.</summary>
    public required string Value { get; init; }
}

public sealed record PropertySetUpdateResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    public required string Handle { get; init; }

    [JsonPropertyName("set_name")]
    public required string SetName { get; init; }

    public required bool Applied { get; init; }

    [JsonPropertyName("changed_count")]
    public int ChangedCount { get; init; }

    public IReadOnlyList<PropertySetValueChange> Properties { get; init; } = Array.Empty<PropertySetValueChange>();

    public WriteTransactionInfo? Transaction { get; init; }
}

/// <summary>
/// One value, before and after. Both halves travel so the caller can show what
/// actually changed rather than only what it now holds.
/// </summary>
public sealed record PropertySetValueChange
{
    public required string Name { get; init; }

    [JsonPropertyName("data_type")]
    public string? DataType { get; init; }

    [JsonPropertyName("previous_value")]
    public string? PreviousValue { get; init; }

    [JsonPropertyName("previous_numeric_value")]
    public double? PreviousNumericValue { get; init; }

    [JsonPropertyName("new_value")]
    public string? NewValue { get; init; }

    [JsonPropertyName("new_numeric_value")]
    public double? NewNumericValue { get; init; }

    public bool Changed { get; init; }
}

/// <summary>
/// Finds the objects that carry a named AEC property set.
///
/// Every other property-set tool takes a handle, which meant they could only be
/// used by someone who already had one. This is the read that produces handles
/// in the first place.
/// </summary>
public sealed record PropertySetFindRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("set_name")]
    public required string SetName { get; init; }

    /// <summary>Value filters, combined with AND. Empty returns every carrier.</summary>
    public IReadOnlyList<PropertySetValueFilter> Match { get; init; } = Array.Empty<PropertySetValueFilter>();

    /// <summary>Maximum objects to return. Results beyond it set `truncated`.</summary>
    public int? Limit { get; init; }

    /// <summary>
    /// How many objects the sweep may examine before stopping.
    ///
    /// This exists because live validation hit the default on a real drawing and
    /// returned a prefix. The platform cannot know how long an operator will
    /// tolerate a busy Civil 3D, so the caller owns the trade-off between
    /// completeness and responsiveness.
    /// </summary>
    [JsonPropertyName("max_scan")]
    public int? MaxScan { get; init; }
}

/// <summary>
/// One condition on one property. Exactly one criterion must be supplied: a
/// filter that constrains nothing would silently widen the result.
/// </summary>
public sealed record PropertySetValueFilter
{
    public required string Name { get; init; }

    /// <summary>
    /// Exact, case-insensitive match against the invariant value text. Named
    /// EqualsValue because a record cannot declare a member called Equals;
    /// the wire name stays `equals`.
    /// </summary>
    [JsonPropertyName("equals")]
    public string? EqualsValue { get; init; }

    /// <summary>Case-insensitive substring match.</summary>
    public string? Contains { get; init; }
}

public sealed record PropertySetFindResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("set_name")]
    public required string SetName { get; init; }

    /// <summary>Objects returned, after filtering and the limit.</summary>
    public int Count { get; init; }

    /// <summary>Objects that matched before the limit was applied.</summary>
    [JsonPropertyName("matched_count")]
    public int MatchedCount { get; init; }

    /// <summary>How many objects the sweep looked at, so a caller can judge coverage.</summary>
    [JsonPropertyName("scanned_count")]
    public int ScannedCount { get; init; }

    /// <summary>
    /// True when the answer is a prefix: either the limit cut it, or the sweep
    /// stopped before the end of the drawing.
    /// </summary>
    public bool Truncated { get; init; }

    public IReadOnlyList<PropertySetMatch> Objects { get; init; } = Array.Empty<PropertySetMatch>();
}

public sealed record PropertySetMatch
{
    public required string Handle { get; init; }

    [JsonPropertyName("object_class")]
    public string? ObjectClass { get; init; }

    [JsonPropertyName("property_set")]
    public required PropertySetInfo PropertySet { get; init; }
}
