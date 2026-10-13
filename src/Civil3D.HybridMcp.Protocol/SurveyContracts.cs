using System.Text.Json.Serialization;

namespace Civil3D.HybridMcp.Protocol;

public sealed record SurveyBearingToAzimuthRequest
{
    public required string Bearing { get; init; }
}

public sealed record SurveyAzimuthToBearingRequest
{
    [JsonPropertyName("azimuth_degrees")]
    public required double AzimuthDegrees { get; init; }
}

public sealed record SurveyDistanceRequest
{
    [JsonPropertyName("start_x")]
    public required double StartX { get; init; }

    [JsonPropertyName("start_y")]
    public required double StartY { get; init; }

    [JsonPropertyName("end_x")]
    public required double EndX { get; init; }

    [JsonPropertyName("end_y")]
    public required double EndY { get; init; }
}

public sealed record SurveyPointByAzimuthRequest
{
    [JsonPropertyName("start_x")]
    public required double StartX { get; init; }

    [JsonPropertyName("start_y")]
    public required double StartY { get; init; }

    [JsonPropertyName("azimuth_degrees")]
    public required double AzimuthDegrees { get; init; }

    public required double Distance { get; init; }
}

public sealed record SurveyFbkDryRunRequest
{
    [JsonPropertyName("source_name")]
    public string? SourceName { get; init; }

    public required string Content { get; init; }
}

public sealed record SurveyDatabaseStatusRequest;

public sealed record SurveyWorkingFolderGetRequest;

public sealed record SurveyProjectListRequest;

public sealed record SurveyNetworkListRequest
{
    [JsonPropertyName("project_name")]
    public string? ProjectName { get; init; }
}

public sealed record SurveyBearingToAzimuthResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("input_bearing")]
    public required string InputBearing { get; init; }

    public required SurveyBearingInfo Bearing { get; init; }

    [JsonPropertyName("azimuth_degrees")]
    public required double AzimuthDegrees { get; init; }
}

public sealed record SurveyAzimuthToBearingResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("azimuth_degrees")]
    public required double AzimuthDegrees { get; init; }

    public required SurveyBearingInfo Bearing { get; init; }
}

public sealed record SurveyDistanceResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("start_x")]
    public required double StartX { get; init; }

    [JsonPropertyName("start_y")]
    public required double StartY { get; init; }

    [JsonPropertyName("end_x")]
    public required double EndX { get; init; }

    [JsonPropertyName("end_y")]
    public required double EndY { get; init; }

    [JsonPropertyName("delta_x")]
    public required double DeltaX { get; init; }

    [JsonPropertyName("delta_y")]
    public required double DeltaY { get; init; }

    public required double Distance { get; init; }

    [JsonPropertyName("azimuth_degrees")]
    public required double AzimuthDegrees { get; init; }
}

public sealed record SurveyPointByAzimuthResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("start_x")]
    public required double StartX { get; init; }

    [JsonPropertyName("start_y")]
    public required double StartY { get; init; }

    [JsonPropertyName("azimuth_degrees")]
    public required double AzimuthDegrees { get; init; }

    public required double Distance { get; init; }

    public required double X { get; init; }

    public required double Y { get; init; }
}

public sealed record SurveyFbkDryRunResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("source_name")]
    public string? SourceName { get; init; }

    [JsonPropertyName("dry_run")]
    public bool DryRun { get; init; } = true;

    [JsonPropertyName("total_lines")]
    public required int TotalLines { get; init; }

    [JsonPropertyName("record_count")]
    public required int RecordCount { get; init; }

    [JsonPropertyName("control_point_count")]
    public required int ControlPointCount { get; init; }

    [JsonPropertyName("setup_count")]
    public required int SetupCount { get; init; }

    [JsonPropertyName("observation_count")]
    public required int ObservationCount { get; init; }

    [JsonPropertyName("warning_count")]
    public required int WarningCount { get; init; }

    public IReadOnlyList<SurveyFbkRecordInfo> Records { get; init; } = Array.Empty<SurveyFbkRecordInfo>();

    [JsonPropertyName("control_points")]
    public IReadOnlyList<SurveyFbkControlPointInfo> ControlPoints { get; init; } = Array.Empty<SurveyFbkControlPointInfo>();

    public IReadOnlyList<SurveyFbkSetupInfo> Setups { get; init; } = Array.Empty<SurveyFbkSetupInfo>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    [JsonPropertyName("supported_keywords")]
    public IReadOnlyList<string> SupportedKeywords { get; init; } = Array.Empty<string>();
}

public sealed record SurveyDatabaseStatusResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("survey_com_available")]
    public required bool SurveyComAvailable { get; init; }

    [JsonPropertyName("survey_database_available")]
    public required bool SurveyDatabaseAvailable { get; init; }

    [JsonPropertyName("requires_active_document")]
    public required bool RequiresActiveDocument { get; init; }

    public required string Adapter { get; init; }

    public required string Status { get; init; }

    [JsonPropertyName("working_folder")]
    public string? WorkingFolder { get; init; }

    [JsonPropertyName("interop_assemblies")]
    public IReadOnlyList<string> InteropAssemblies { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
}

public sealed record SurveyWorkingFolderGetResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    public required string Adapter { get; init; }

    public required string Status { get; init; }

    [JsonPropertyName("working_folder")]
    public string? WorkingFolder { get; init; }

    [JsonPropertyName("requires_active_document")]
    public required bool RequiresActiveDocument { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
}

public sealed record SurveyProjectListResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    public required string Adapter { get; init; }

    public required string Status { get; init; }

    [JsonPropertyName("working_folder")]
    public string? WorkingFolder { get; init; }

    public required int Count { get; init; }

    public IReadOnlyList<SurveyProjectInfo> Projects { get; init; } = Array.Empty<SurveyProjectInfo>();

    [JsonPropertyName("requires_active_document")]
    public required bool RequiresActiveDocument { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
}

public sealed record SurveyProjectInfo
{
    public required string Name { get; init; }

    public string? Path { get; init; }

    [JsonPropertyName("project_guid")]
    public string? ProjectGuid { get; init; }
}

public sealed record SurveyNetworkListResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    public required string Adapter { get; init; }

    public required string Status { get; init; }

    [JsonPropertyName("project_name")]
    public string? ProjectName { get; init; }

    [JsonPropertyName("working_folder")]
    public string? WorkingFolder { get; init; }

    public required int Count { get; init; }

    public IReadOnlyList<SurveyNetworkInfo> Networks { get; init; } = Array.Empty<SurveyNetworkInfo>();

    [JsonPropertyName("requires_active_document")]
    public required bool RequiresActiveDocument { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed record SurveyNetworkInfo
{
    public required string Name { get; init; }

    [JsonPropertyName("project_name")]
    public string? ProjectName { get; init; }

    public string? Description { get; init; }

    public string? Path { get; init; }

    public int? Id { get; init; }
}

public sealed record SurveyBearingInfo
{
    [JsonPropertyName("north_south")]
    public required string NorthSouth { get; init; }

    [JsonPropertyName("angle_degrees")]
    public required double AngleDegrees { get; init; }

    [JsonPropertyName("east_west")]
    public required string EastWest { get; init; }

    public required string Text { get; init; }
}

public sealed record SurveyFbkRecordInfo
{
    [JsonPropertyName("line_number")]
    public required int LineNumber { get; init; }

    public required string Keyword { get; init; }

    [JsonPropertyName("record_type")]
    public required string RecordType { get; init; }

    [JsonPropertyName("point_number")]
    public int? PointNumber { get; init; }

    public double? Northing { get; init; }

    public double? Easting { get; init; }

    public double? Elevation { get; init; }

    [JsonPropertyName("instrument_height")]
    public double? InstrumentHeight { get; init; }

    public double? Circle { get; init; }

    [JsonPropertyName("az_from")]
    public int? AzFrom { get; init; }

    [JsonPropertyName("az_to")]
    public int? AzTo { get; init; }

    [JsonPropertyName("azimuth_dms")]
    public double? AzimuthDms { get; init; }

    [JsonPropertyName("prism_height")]
    public double? PrismHeight { get; init; }

    [JsonPropertyName("angle_flavor")]
    public string? AngleFlavor { get; init; }

    [JsonPropertyName("horizontal_angle")]
    public double? HorizontalAngle { get; init; }

    [JsonPropertyName("slope_distance")]
    public double? SlopeDistance { get; init; }

    [JsonPropertyName("vertical_angle")]
    public double? VerticalAngle { get; init; }

    public string? Description { get; init; }

    public string? Warning { get; init; }
}

public sealed record SurveyFbkControlPointInfo
{
    [JsonPropertyName("point_number")]
    public required int PointNumber { get; init; }

    public required double Northing { get; init; }

    public required double Easting { get; init; }

    public required double Elevation { get; init; }

    public string? Description { get; init; }
}

public sealed record SurveyFbkSetupInfo
{
    [JsonPropertyName("station_point")]
    public required int StationPoint { get; init; }

    [JsonPropertyName("instrument_height")]
    public required double InstrumentHeight { get; init; }

    public string? Description { get; init; }

    [JsonPropertyName("backsight_point")]
    public int? BacksightPoint { get; init; }

    [JsonPropertyName("backsight_circle_dms")]
    public double? BacksightCircleDms { get; init; }

    [JsonPropertyName("azimuth_dms")]
    public double? AzimuthDms { get; init; }

    [JsonPropertyName("az_from")]
    public int? AzFrom { get; init; }

    [JsonPropertyName("az_to")]
    public int? AzTo { get; init; }

    [JsonPropertyName("observation_count")]
    public required int ObservationCount { get; init; }

    [JsonPropertyName("observation_lines")]
    public IReadOnlyList<int> ObservationLines { get; init; } = Array.Empty<int>();
}
