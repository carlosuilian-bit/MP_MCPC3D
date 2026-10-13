using System.Text.Json.Serialization;

namespace Civil3D.HybridMcp.Protocol;

/// <summary>
/// Block reads: definitions, inserted references, and attribute values.
///
/// Blocks are plain AutoCAD entities, so these reads work on a drawing in either
/// project moment: geometric, where Civil 3D objects are alive, and exported,
/// where the project has been reduced to AutoCAD primitives.
///
/// Every field carries an explicit JSON name. The platform's default naming
/// policy is camelCase, so a field without one would come out camelCase beside
/// snake_case neighbours; see defect D2 in docs/CONSOLIDATION_REVIEW.md.
/// </summary>
public sealed record BlockListRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("name_contains")]
    public string? NameContains { get; init; }

    /// <summary>Anonymous definitions (*U, *D, *X...) are hidden unless asked for.</summary>
    [JsonPropertyName("include_anonymous")]
    public bool? IncludeAnonymous { get; init; }

    /// <summary>Model space and paper space layouts are block table records too; hidden unless asked for.</summary>
    [JsonPropertyName("include_layouts")]
    public bool? IncludeLayouts { get; init; }
}

public sealed record BlockListResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("blocks")]
    public IReadOnlyList<BlockDefinitionInfo> Blocks { get; init; } = Array.Empty<BlockDefinitionInfo>();
}

public sealed record BlockDefinitionInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    [JsonPropertyName("is_anonymous")]
    public bool IsAnonymous { get; init; }

    [JsonPropertyName("is_dynamic")]
    public bool IsDynamic { get; init; }

    [JsonPropertyName("is_xref")]
    public bool IsXref { get; init; }

    [JsonPropertyName("is_overlay")]
    public bool IsOverlay { get; init; }

    [JsonPropertyName("is_layout")]
    public bool IsLayout { get; init; }

    /// <summary>True for a definition that belongs to an attached xref, named as XREF|BLOCK.</summary>
    [JsonPropertyName("is_dependent")]
    public bool IsDependent { get; init; }

    [JsonPropertyName("has_attribute_definitions")]
    public bool HasAttributeDefinitions { get; init; }

    [JsonPropertyName("attribute_definitions")]
    public IReadOnlyList<BlockAttributeDefinitionInfo> AttributeDefinitions { get; init; } = Array.Empty<BlockAttributeDefinitionInfo>();

    /// <summary>
    /// Inserts of this definition anywhere in the drawing. For a dynamic block
    /// this includes inserts that point at its anonymous representations, which
    /// is where most dynamic inserts actually point.
    /// </summary>
    [JsonPropertyName("reference_count")]
    public int ReferenceCount { get; init; }

    [JsonPropertyName("comments")]
    public string? Comments { get; init; }

    [JsonPropertyName("units")]
    public string? Units { get; init; }
}

public sealed record BlockAttributeDefinitionInfo
{
    [JsonPropertyName("tag")]
    public required string Tag { get; init; }

    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    [JsonPropertyName("default_value")]
    public string? DefaultValue { get; init; }

    /// <summary>A constant attribute has its value fixed on the definition and never appears on an insert.</summary>
    [JsonPropertyName("constant")]
    public bool Constant { get; init; }

    [JsonPropertyName("invisible")]
    public bool Invisible { get; init; }

    [JsonPropertyName("multiline")]
    public bool Multiline { get; init; }
}

public sealed record BlockReferenceListRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    /// <summary>
    /// Exact block name, case-insensitive. Matches the effective name of a
    /// dynamic block as well as the definition name, so a dynamic insert whose
    /// definition is *U12 is still found by its real name.
    /// </summary>
    [JsonPropertyName("block_name")]
    public string? BlockName { get; init; }

    [JsonPropertyName("layer")]
    public string? Layer { get; init; }

    /// <summary>Inserts of anonymous blocks are hidden unless asked for, or unless named in <see cref="BlockName"/>.</summary>
    [JsonPropertyName("include_anonymous")]
    public bool? IncludeAnonymous { get; init; }

    [JsonPropertyName("include_attributes")]
    public bool? IncludeAttributes { get; init; }

    /// <summary>Entities to inspect before stopping. Defaults to a budget large enough for production drawings.</summary>
    [JsonPropertyName("max_scan")]
    public int? MaxScan { get; init; }

    [JsonPropertyName("max_results")]
    public int? MaxResults { get; init; }
}

public sealed record BlockReferenceListResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("scanned_count")]
    public int ScannedCount { get; init; }

    /// <summary>True when the list is a prefix of the answer, not the whole answer.</summary>
    [JsonPropertyName("truncated")]
    public bool Truncated { get; init; }

    /// <summary><c>scan_budget</c> or <c>result_limit</c> when truncated; null otherwise.</summary>
    [JsonPropertyName("truncated_by")]
    public string? TruncatedBy { get; init; }

    [JsonPropertyName("references")]
    public IReadOnlyList<BlockReferenceInfo> References { get; init; } = Array.Empty<BlockReferenceInfo>();
}

public sealed record BlockReferenceInfo
{
    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    /// <summary>The name a person knows the block by: the dynamic block's own name for a dynamic insert.</summary>
    [JsonPropertyName("block_name")]
    public required string BlockName { get; init; }

    /// <summary>The definition the insert actually points at, which for a dynamic insert is often anonymous.</summary>
    [JsonPropertyName("definition_name")]
    public required string DefinitionName { get; init; }

    [JsonPropertyName("is_dynamic")]
    public bool IsDynamic { get; init; }

    [JsonPropertyName("is_anonymous")]
    public bool IsAnonymous { get; init; }

    [JsonPropertyName("layer")]
    public string? Layer { get; init; }

    /// <summary>Layout that owns the insert: <c>Model</c> or a paper space layout name.</summary>
    [JsonPropertyName("space")]
    public string? Space { get; init; }

    [JsonPropertyName("x")]
    public double X { get; init; }

    [JsonPropertyName("y")]
    public double Y { get; init; }

    [JsonPropertyName("z")]
    public double Z { get; init; }

    [JsonPropertyName("rotation_radians")]
    public double RotationRadians { get; init; }

    [JsonPropertyName("scale_x")]
    public double ScaleX { get; init; }

    [JsonPropertyName("scale_y")]
    public double ScaleY { get; init; }

    [JsonPropertyName("scale_z")]
    public double ScaleZ { get; init; }

    /// <summary>Attributes carried by the insert itself. Constant attributes are not counted here.</summary>
    [JsonPropertyName("attribute_count")]
    public int AttributeCount { get; init; }

    /// <summary>Null unless attributes were requested.</summary>
    [JsonPropertyName("attributes")]
    public IReadOnlyList<BlockAttributeInfo>? Attributes { get; init; }
}

public sealed record BlockAttributeInfo
{
    [JsonPropertyName("tag")]
    public required string Tag { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    /// <summary>Handle of the attribute reference; null for a constant attribute, which has no reference.</summary>
    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    [JsonPropertyName("constant")]
    public bool Constant { get; init; }

    [JsonPropertyName("invisible")]
    public bool Invisible { get; init; }

    [JsonPropertyName("multiline")]
    public bool Multiline { get; init; }
}

public sealed record BlockGetAttributesRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    [JsonPropertyName("object_reference")]
    public CadObjectReference? ObjectReference { get; init; }
}

public sealed record BlockGetAttributesResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("handle")]
    public required string Handle { get; init; }

    [JsonPropertyName("block_name")]
    public required string BlockName { get; init; }

    [JsonPropertyName("definition_name")]
    public required string DefinitionName { get; init; }

    [JsonPropertyName("is_dynamic")]
    public bool IsDynamic { get; init; }

    [JsonPropertyName("count")]
    public int Count { get; init; }

    /// <summary>Attributes carried by the insert, followed by the definition's constant attributes.</summary>
    [JsonPropertyName("attributes")]
    public IReadOnlyList<BlockAttributeInfo> Attributes { get; init; } = Array.Empty<BlockAttributeInfo>();
}
