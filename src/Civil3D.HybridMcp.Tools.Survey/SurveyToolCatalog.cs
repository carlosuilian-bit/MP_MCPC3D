using System.Text.Json.Nodes;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Tools.Survey;

public static class SurveyToolCatalog
{
    public static IReadOnlyList<ToolDescriptor> ListTools() =>
    [
        ReadTool(SurveyToolNames.BearingToAzimuth, "Convert a quadrant bearing to a normalized survey azimuth.", SurveySchemas.BearingToAzimuthInput(), nameof(SurveyBearingToAzimuthResponse)),
        ReadTool(SurveyToolNames.AzimuthToBearing, "Convert a survey azimuth to a quadrant bearing.", SurveySchemas.AzimuthToBearingInput(), nameof(SurveyAzimuthToBearingResponse)),
        ReadTool(SurveyToolNames.Distance, "Compute distance, deltas, and survey azimuth between two XY coordinates.", SurveySchemas.DistanceInput(), nameof(SurveyDistanceResponse)),
        ReadTool(SurveyToolNames.PointByAzimuth, "Project an XY coordinate by survey azimuth and distance.", SurveySchemas.PointByAzimuthInput(), nameof(SurveyPointByAzimuthResponse)),
        ReadTool(SurveyToolNames.FbkDryRun, "Parse FBK content and return a dry-run Survey import preview without touching Civil 3D.", SurveySchemas.FbkDryRunInput(), nameof(SurveyFbkDryRunResponse), TimeSpan.FromSeconds(10)),
        ReadTool(SurveyToolNames.DatabaseStatus, "Report Survey Database adapter readiness without opening, creating, importing, or mutating Survey data.", SurveySchemas.DatabaseStatusInput(), nameof(SurveyDatabaseStatusResponse)),
        ReadTool(SurveyToolNames.WorkingFolderGet, "Read the Civil 3D Survey working folder through a typed read-only adapter.", SurveySchemas.WorkingFolderGetInput(), nameof(SurveyWorkingFolderGetResponse), TimeSpan.FromSeconds(10)),
        ReadTool(SurveyToolNames.ProjectList, "List Civil 3D Survey projects from the current Survey working folder through a typed read-only adapter.", SurveySchemas.ProjectListInput(), nameof(SurveyProjectListResponse), TimeSpan.FromSeconds(10)),
        ReadTool(SurveyToolNames.NetworkList, "List Civil 3D Survey networks for one or all Survey projects through a typed read-only adapter. Does not open, create, or modify Survey Databases.", SurveySchemas.NetworkListInput(), nameof(SurveyNetworkListResponse), TimeSpan.FromSeconds(10))
    ];

    private static ToolDescriptor ReadTool(string name, string description, JsonObject inputSchema, string outputTitle, TimeSpan? timeout = null) =>
        new()
        {
            Name = name,
            Description = description,
            OperationClass = OperationClass.Read,
            Risk = ToolRisk.Low,
            InputSchema = inputSchema,
            OutputSchema = ToolSchema.ObjectOutput(outputTitle),
            Timeout = timeout ?? TimeSpan.FromSeconds(5),
            RequiresConfirmation = false,
            RollbackSemantics = RollbackSemantics.NoneRequired,
            Module = "Survey",
            Version = ProtocolVersion.Current
        };
}

public static class SurveyToolNames
{
    public const string BearingToAzimuth = "survey_bearing_to_azimuth";
    public const string AzimuthToBearing = "survey_azimuth_to_bearing";
    public const string Distance = "survey_distance";
    public const string PointByAzimuth = "survey_point_by_azimuth";
    public const string FbkDryRun = "survey_fbk_dry_run";
    public const string DatabaseStatus = "survey_database_status";
    public const string WorkingFolderGet = "survey_working_folder_get";
    public const string ProjectList = "survey_project_list";
    public const string NetworkList = "survey_network_list";
}

public static class SurveySchemas
{
    public static JsonObject BearingToAzimuthInput() =>
        ObjectSchema(
            new JsonObject
            {
                ["bearing"] = StringProperty("Quadrant bearing, for example 'N 45 30 0 E'.")
            },
            ["bearing"]);

    public static JsonObject AzimuthToBearingInput() =>
        ObjectSchema(
            new JsonObject
            {
                ["azimuth_degrees"] = NumberProperty("Survey azimuth in degrees. Values are normalized to 0 through less than 360.")
            },
            ["azimuth_degrees"]);

    public static JsonObject DistanceInput() =>
        ObjectSchema(
            new JsonObject
            {
                ["start_x"] = NumberProperty("Starting easting or drawing X coordinate."),
                ["start_y"] = NumberProperty("Starting northing or drawing Y coordinate."),
                ["end_x"] = NumberProperty("Ending easting or drawing X coordinate."),
                ["end_y"] = NumberProperty("Ending northing or drawing Y coordinate.")
            },
            ["start_x", "start_y", "end_x", "end_y"]);

    public static JsonObject PointByAzimuthInput() =>
        ObjectSchema(
            new JsonObject
            {
                ["start_x"] = NumberProperty("Starting easting or drawing X coordinate."),
                ["start_y"] = NumberProperty("Starting northing or drawing Y coordinate."),
                ["azimuth_degrees"] = NumberProperty("Survey azimuth in degrees."),
                ["distance"] = NumberProperty("Projection distance. Must be zero or positive.")
            },
            ["start_x", "start_y", "azimuth_degrees", "distance"]);

    public static JsonObject FbkDryRunInput() =>
        ObjectSchema(
            new JsonObject
            {
                ["source_name"] = StringProperty("Optional display name for the FBK content, for example 'topo.fbk'."),
                ["content"] = StringProperty("FBK text content to parse. The bridge does not read files in this dry-run tool.")
            },
            ["content"]);

    public static JsonObject DatabaseStatusInput() =>
        ObjectSchema(new JsonObject(), []);

    public static JsonObject WorkingFolderGetInput() =>
        ObjectSchema(new JsonObject(), []);

    public static JsonObject ProjectListInput() =>
        ObjectSchema(new JsonObject(), []);

    public static JsonObject NetworkListInput() =>
        ObjectSchema(
            new JsonObject
            {
                ["project_name"] = StringProperty("Optional Survey project name. When omitted, networks from every Survey project are listed.")
            },
            []);

    private static JsonObject ObjectSchema(JsonObject properties, IReadOnlyList<string> required) =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray(required.Select(item => JsonValue.Create(item)).ToArray<JsonNode?>()),
            ["properties"] = properties
        };

    private static JsonObject StringProperty(string description) =>
        new()
        {
            ["type"] = "string",
            ["description"] = description
        };

    private static JsonObject NumberProperty(string description) =>
        new()
        {
            ["type"] = "number",
            ["description"] = description
        };
}
