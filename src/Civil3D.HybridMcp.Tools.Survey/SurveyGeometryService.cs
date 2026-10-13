using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Tools.Survey;

public sealed class SurveyGeometryService
{
    private readonly ISurveyDatabaseGateway _databaseGateway;

    public SurveyGeometryService()
        : this(UnavailableSurveyDatabaseGateway.Instance)
    {
    }

    public SurveyGeometryService(ISurveyDatabaseGateway databaseGateway)
    {
        _databaseGateway = databaseGateway;
    }

    public SurveyBearingToAzimuthResponse BearingToAzimuth(string instanceId, SurveyBearingToAzimuthRequest request)
    {
        ValidateInstance(instanceId);
        var bearing = SurveyGeometry.ParseBearing(request.Bearing);
        return new SurveyBearingToAzimuthResponse
        {
            InstanceId = instanceId,
            InputBearing = request.Bearing,
            Bearing = bearing,
            AzimuthDegrees = SurveyGeometry.BearingToAzimuth(bearing)
        };
    }

    public SurveyAzimuthToBearingResponse AzimuthToBearing(string instanceId, SurveyAzimuthToBearingRequest request)
    {
        ValidateInstance(instanceId);
        var azimuth = SurveyGeometry.NormalizeAzimuth(request.AzimuthDegrees);
        return new SurveyAzimuthToBearingResponse
        {
            InstanceId = instanceId,
            AzimuthDegrees = azimuth,
            Bearing = SurveyGeometry.AzimuthToBearing(azimuth)
        };
    }

    public SurveyDistanceResponse Distance(string instanceId, SurveyDistanceRequest request)
    {
        ValidateInstance(instanceId);
        ValidateFinite(request.StartX, nameof(request.StartX));
        ValidateFinite(request.StartY, nameof(request.StartY));
        ValidateFinite(request.EndX, nameof(request.EndX));
        ValidateFinite(request.EndY, nameof(request.EndY));

        var deltaX = request.EndX - request.StartX;
        var deltaY = request.EndY - request.StartY;
        var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));

        return new SurveyDistanceResponse
        {
            InstanceId = instanceId,
            StartX = request.StartX,
            StartY = request.StartY,
            EndX = request.EndX,
            EndY = request.EndY,
            DeltaX = deltaX,
            DeltaY = deltaY,
            Distance = distance,
            AzimuthDegrees = SurveyGeometry.DeltasToAzimuth(deltaX, deltaY)
        };
    }

    public SurveyPointByAzimuthResponse PointByAzimuth(string instanceId, SurveyPointByAzimuthRequest request)
    {
        ValidateInstance(instanceId);
        ValidateFinite(request.StartX, nameof(request.StartX));
        ValidateFinite(request.StartY, nameof(request.StartY));
        ValidateFinite(request.Distance, nameof(request.Distance));
        if (request.Distance < 0)
        {
            throw new SurveyToolException(ErrorCode.InvalidRequest, "distance must be zero or positive.");
        }

        var azimuth = SurveyGeometry.NormalizeAzimuth(request.AzimuthDegrees);
        var radians = SurveyGeometry.ToRadians(azimuth);
        var deltaX = request.Distance * Math.Sin(radians);
        var deltaY = request.Distance * Math.Cos(radians);

        return new SurveyPointByAzimuthResponse
        {
            InstanceId = instanceId,
            StartX = request.StartX,
            StartY = request.StartY,
            AzimuthDegrees = azimuth,
            Distance = request.Distance,
            X = request.StartX + deltaX,
            Y = request.StartY + deltaY
        };
    }

    public SurveyFbkDryRunResponse FbkDryRun(string instanceId, SurveyFbkDryRunRequest request)
    {
        ValidateInstance(instanceId);
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            throw new SurveyToolException(ErrorCode.InvalidRequest, "content is required.");
        }

        return SurveyFbkParser.Parse(instanceId, request);
    }

    public SurveyDatabaseStatusResponse DatabaseStatus(string instanceId, SurveyDatabaseStatusRequest request)
    {
        ValidateInstance(instanceId);
        ArgumentNullException.ThrowIfNull(request);
        return _databaseGateway.GetStatus(instanceId) with { InstanceId = instanceId };
    }

    public SurveyWorkingFolderGetResponse WorkingFolderGet(string instanceId, SurveyWorkingFolderGetRequest request)
    {
        ValidateInstance(instanceId);
        ArgumentNullException.ThrowIfNull(request);
        return _databaseGateway.GetWorkingFolder(instanceId) with { InstanceId = instanceId };
    }

    public SurveyProjectListResponse ProjectList(string instanceId, SurveyProjectListRequest request)
    {
        ValidateInstance(instanceId);
        ArgumentNullException.ThrowIfNull(request);
        return _databaseGateway.ListProjects(instanceId) with { InstanceId = instanceId };
    }

    public SurveyNetworkListResponse NetworkList(string instanceId, SurveyNetworkListRequest request)
    {
        ValidateInstance(instanceId);
        ArgumentNullException.ThrowIfNull(request);
        var projectName = string.IsNullOrWhiteSpace(request.ProjectName) ? null : request.ProjectName.Trim();
        return _databaseGateway.ListNetworks(instanceId, projectName) with { InstanceId = instanceId };
    }

    private static void ValidateInstance(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new SurveyToolException(ErrorCode.InvalidRequest, "instance_id is required.");
        }
    }

    private static void ValidateFinite(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new SurveyToolException(ErrorCode.InvalidRequest, $"{name} must be a finite number.");
        }
    }
}

public interface ISurveyDatabaseGateway
{
    SurveyDatabaseStatusResponse GetStatus(string instanceId);
    SurveyWorkingFolderGetResponse GetWorkingFolder(string instanceId);
    SurveyProjectListResponse ListProjects(string instanceId);
    SurveyNetworkListResponse ListNetworks(string instanceId, string? projectName);
}

public sealed class UnavailableSurveyDatabaseGateway : ISurveyDatabaseGateway
{
    public static UnavailableSurveyDatabaseGateway Instance { get; } = new();

    private UnavailableSurveyDatabaseGateway()
    {
    }

    public SurveyDatabaseStatusResponse GetStatus(string instanceId) =>
        new()
        {
            InstanceId = instanceId,
            SurveyComAvailable = false,
            SurveyDatabaseAvailable = false,
            RequiresActiveDocument = true,
            Adapter = "none",
            Status = "not_configured",
            Capabilities = [],
            Limitations =
            [
                "Survey Database adapter is not configured in this bridge.",
                "Survey Database reads will be enabled behind a typed COM adapter in a later Phase 4C slice."
            ]
        };

    public SurveyWorkingFolderGetResponse GetWorkingFolder(string instanceId) =>
        throw new SurveyToolException(
            ErrorCode.NotSupported,
            "Survey working folder read requires the Autodesk bridge Survey COM adapter.");

    public SurveyProjectListResponse ListProjects(string instanceId) =>
        throw new SurveyToolException(
            ErrorCode.NotSupported,
            "Survey project listing requires the Autodesk bridge Survey COM adapter.");

    public SurveyNetworkListResponse ListNetworks(string instanceId, string? projectName) =>
        throw new SurveyToolException(
            ErrorCode.NotSupported,
            "Survey network listing requires the Autodesk bridge Survey COM adapter.");
}

public sealed class SurveyDatabaseEnvironmentGateway : ISurveyDatabaseGateway
{
    private static readonly string[] RequiredFiles =
    [
        "Autodesk.AECC.Interop.Survey.dll",
        "Autodesk.AECC.Interop.UiSurvey.dll",
        "AeccXSurvey.dll",
        "AeccSurvey.dbx"
    ];

    private static readonly string[] InteropAssemblyNames =
    [
        "Autodesk.AECC.Interop.Survey.dll",
        "Autodesk.AECC.Interop.UiSurvey.dll"
    ];

    private readonly string? _civil3DDirectory;

    public SurveyDatabaseEnvironmentGateway(string? civil3DDirectory)
    {
        _civil3DDirectory = ResolveCivil3DDirectory(civil3DDirectory);
    }

    public SurveyDatabaseStatusResponse GetStatus(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(_civil3DDirectory) || !Directory.Exists(_civil3DDirectory))
        {
            return Unavailable("Civil 3D C3D directory was not found; Survey COM interop cannot be detected.");
        }

        var foundRequiredFiles = RequiredFiles
            .Where(name => File.Exists(Path.Combine(_civil3DDirectory, name)))
            .ToArray();
        var foundInteropAssemblies = InteropAssemblyNames
            .Where(name => File.Exists(Path.Combine(_civil3DDirectory, name)))
            .ToArray();
        var missingRequiredFiles = RequiredFiles.Except(foundRequiredFiles, StringComparer.OrdinalIgnoreCase).ToArray();

        if (missingRequiredFiles.Length > 0)
        {
            return new SurveyDatabaseStatusResponse
            {
                InstanceId = instanceId,
                SurveyComAvailable = false,
                SurveyDatabaseAvailable = false,
                RequiresActiveDocument = true,
                Adapter = "environment_probe",
                Status = "interop_incomplete",
                InteropAssemblies = foundInteropAssemblies,
                Capabilities = [],
                Limitations =
                [
                    $"Missing Survey COM runtime files: {string.Join(", ", missingRequiredFiles)}.",
                    "This probe does not initialize COM, open Survey Databases, or read the Survey working folder."
                ]
            };
        }

        return new SurveyDatabaseStatusResponse
        {
            InstanceId = instanceId,
            SurveyComAvailable = true,
            SurveyDatabaseAvailable = false,
            RequiresActiveDocument = true,
            Adapter = "environment_probe",
            Status = "interop_available",
            InteropAssemblies = foundInteropAssemblies,
            Capabilities =
            [
                "survey_com_interop_available"
            ],
            Limitations =
            [
                "Survey COM interop files are present, but this probe does not initialize COM.",
                "Survey Database reads still require a typed adapter running on the Civil 3D host context.",
                "Working folder, project, network, setup, observation, and figure reads are not enabled yet."
            ]
        };
    }

    public SurveyWorkingFolderGetResponse GetWorkingFolder(string instanceId) =>
        throw new SurveyToolException(
            ErrorCode.NotSupported,
            "The environment probe can detect Survey runtime files, but a typed Survey COM adapter is required to read the working folder.");

    public SurveyProjectListResponse ListProjects(string instanceId) =>
        throw new SurveyToolException(
            ErrorCode.NotSupported,
            "The environment probe can detect Survey runtime files, but a typed Survey COM adapter is required to list Survey projects.");

    public SurveyNetworkListResponse ListNetworks(string instanceId, string? projectName) =>
        throw new SurveyToolException(
            ErrorCode.NotSupported,
            "The environment probe can detect Survey runtime files, but a typed Survey COM adapter is required to list Survey networks.");

    private static SurveyDatabaseStatusResponse Unavailable(string limitation) =>
        new()
        {
            InstanceId = string.Empty,
            SurveyComAvailable = false,
            SurveyDatabaseAvailable = false,
            RequiresActiveDocument = true,
            Adapter = "environment_probe",
            Status = "interop_not_found",
            InteropAssemblies = [],
            Capabilities = [],
            Limitations =
            [
                limitation,
                "This probe does not initialize COM, open Survey Databases, or read the Survey working folder."
            ]
        };

    private static string? ResolveCivil3DDirectory(string? candidateDirectory)
    {
        if (string.IsNullOrWhiteSpace(candidateDirectory))
        {
            return null;
        }

        var trimmed = candidateDirectory.Trim();
        var c3dSubdirectory = Path.Combine(trimmed, "C3D");
        if (Directory.Exists(c3dSubdirectory))
        {
            return c3dSubdirectory;
        }

        return trimmed;
    }
}

public sealed class SurveyToolHandler : IToolHandler
{
    private readonly ToolDescriptor _descriptor;
    private readonly string _instanceId;
    private readonly SurveyGeometryService _service;
    private readonly IHostApplicationDispatcher _dispatcher;
    private readonly Func<DocumentDescriptor?>? _activeDocumentAccessor;

    public SurveyToolHandler(
        ToolDescriptor descriptor,
        string instanceId,
        SurveyGeometryService service,
        IHostApplicationDispatcher? dispatcher = null,
        Func<DocumentDescriptor?>? activeDocumentAccessor = null)
    {
        _descriptor = descriptor;
        _instanceId = instanceId;
        _service = service;
        _dispatcher = dispatcher ?? new InlineHostApplicationDispatcher();
        _activeDocumentAccessor = activeDocumentAccessor;
    }

    public ToolDescriptor Descriptor => _descriptor;

    public async Task<ToolResponse> ExecuteAsync(ToolRequest request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(request.InstanceId, _instanceId, StringComparison.Ordinal))
            {
                throw new SurveyToolException(ErrorCode.InstanceNotFound, "Survey request instance_id does not match this bridge.");
            }

            var result = RequiresHostContext(request.ToolName)
                ? await _dispatcher.ExecuteAsync(_ => ExecuteWithDocument(request), cancellationToken)
                : ExecuteWithDocument(request);

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
        catch (SurveyToolException ex)
        {
            return Error(request, ex.Code, ex.Message, started, ex.Details);
        }
        catch (JsonException ex)
        {
            return Error(request, ErrorCode.InvalidRequest, ex.Message, started);
        }
        catch (Exception ex)
        {
            return Error(request, ErrorCode.InternalError, ex.Message, started);
        }
    }

    private static bool RequiresHostContext(string toolName) =>
        string.Equals(toolName, SurveyToolNames.WorkingFolderGet, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(toolName, SurveyToolNames.ProjectList, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(toolName, SurveyToolNames.NetworkList, StringComparison.OrdinalIgnoreCase);

    private ToolExecutionResult ExecuteWithDocument(ToolRequest request)
    {
        var data = Execute(request);
        var document = RequiresHostContext(request.ToolName) ? _activeDocumentAccessor?.Invoke() : null;
        return new ToolExecutionResult(document, data);
    }

    private object Execute(ToolRequest request) =>
        request.ToolName.ToLowerInvariant() switch
        {
            SurveyToolNames.BearingToAzimuth => _service.BearingToAzimuth(_instanceId, ReadArguments<SurveyBearingToAzimuthRequest>(request)),
            SurveyToolNames.AzimuthToBearing => _service.AzimuthToBearing(_instanceId, ReadArguments<SurveyAzimuthToBearingRequest>(request)),
            SurveyToolNames.Distance => _service.Distance(_instanceId, ReadArguments<SurveyDistanceRequest>(request)),
            SurveyToolNames.PointByAzimuth => _service.PointByAzimuth(_instanceId, ReadArguments<SurveyPointByAzimuthRequest>(request)),
            SurveyToolNames.FbkDryRun => _service.FbkDryRun(_instanceId, ReadArguments<SurveyFbkDryRunRequest>(request)),
            SurveyToolNames.DatabaseStatus => _service.DatabaseStatus(_instanceId, ReadOptionalArguments<SurveyDatabaseStatusRequest>(request)),
            SurveyToolNames.WorkingFolderGet => _service.WorkingFolderGet(_instanceId, ReadOptionalArguments<SurveyWorkingFolderGetRequest>(request)),
            SurveyToolNames.ProjectList => _service.ProjectList(_instanceId, ReadOptionalArguments<SurveyProjectListRequest>(request)),
            SurveyToolNames.NetworkList => _service.NetworkList(_instanceId, ReadOptionalArguments<SurveyNetworkListRequest>(request)),
            _ => throw new SurveyToolException(ErrorCode.ToolNotFound, $"Tool '{request.ToolName}' is not registered by the Survey handler.")
        };

    private static T ReadArguments<T>(ToolRequest request)
    {
        if (request.Arguments is null)
        {
            throw new SurveyToolException(ErrorCode.InvalidRequest, "Survey tool request requires structured arguments.");
        }

        return request.Arguments.Deserialize<T>(JsonContractOptions.Default)
            ?? throw new SurveyToolException(ErrorCode.InvalidRequest, "Survey tool request arguments could not be read.");
    }

    private static T ReadOptionalArguments<T>(ToolRequest request)
        where T : new()
    {
        if (request.Arguments is null)
        {
            return new T();
        }

        return request.Arguments.Deserialize<T>(JsonContractOptions.Default)
            ?? throw new SurveyToolException(ErrorCode.InvalidRequest, "Survey tool request arguments could not be read.");
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

    private sealed record ToolExecutionResult(DocumentDescriptor? Document, object Data);
}

public static class SurveyGeometry
{
    private static readonly Regex NumberRegex = new(@"[-+]?\d+(?:\.\d+)?", RegexOptions.Compiled);

    public static SurveyBearingInfo ParseBearing(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SurveyToolException(ErrorCode.InvalidRequest, "bearing is required.");
        }

        var trimmed = value.Trim().ToUpperInvariant();
        var first = trimmed.FirstOrDefault(char.IsLetter);
        var last = trimmed.LastOrDefault(char.IsLetter);
        if ((first is not 'N' and not 'S') || (last is not 'E' and not 'W'))
        {
            throw new SurveyToolException(ErrorCode.InvalidRequest, "bearing must start with N/S and end with E/W.");
        }

        var firstIndex = trimmed.IndexOf(first);
        var lastIndex = trimmed.LastIndexOf(last);
        if (lastIndex <= firstIndex)
        {
            throw new SurveyToolException(ErrorCode.InvalidRequest, "bearing angle is required.");
        }

        var angleText = trimmed.Substring(firstIndex + 1, lastIndex - firstIndex - 1);
        var numbers = NumberRegex.Matches(angleText)
            .Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToArray();

        if (numbers.Length is 0 or > 3)
        {
            throw new SurveyToolException(ErrorCode.InvalidRequest, "bearing angle must include degrees and optional minutes/seconds.");
        }

        var degrees = numbers[0];
        var minutes = numbers.Length > 1 ? numbers[1] : 0;
        var seconds = numbers.Length > 2 ? numbers[2] : 0;
        if (degrees < 0 || minutes < 0 || minutes >= 60 || seconds < 0 || seconds >= 60)
        {
            throw new SurveyToolException(ErrorCode.InvalidRequest, "bearing degrees, minutes, and seconds are out of range.");
        }

        var angle = degrees + (minutes / 60d) + (seconds / 3600d);
        if (angle is < 0 or > 90)
        {
            throw new SurveyToolException(ErrorCode.InvalidRequest, "bearing angle must be between 0 and 90 degrees.");
        }

        return new SurveyBearingInfo
        {
            NorthSouth = first.ToString(),
            AngleDegrees = angle,
            EastWest = last.ToString(),
            Text = $"{first} {FormatAngle(angle)} {last}"
        };
    }

    public static double BearingToAzimuth(SurveyBearingInfo bearing) =>
        (bearing.NorthSouth, bearing.EastWest) switch
        {
            ("N", "E") => NormalizeAzimuth(bearing.AngleDegrees),
            ("S", "E") => NormalizeAzimuth(180 - bearing.AngleDegrees),
            ("S", "W") => NormalizeAzimuth(180 + bearing.AngleDegrees),
            ("N", "W") => NormalizeAzimuth(360 - bearing.AngleDegrees),
            _ => throw new SurveyToolException(ErrorCode.InvalidRequest, "bearing directions are invalid.")
        };

    public static SurveyBearingInfo AzimuthToBearing(double azimuthDegrees)
    {
        var azimuth = NormalizeAzimuth(azimuthDegrees);
        if (azimuth <= 90)
        {
            return CreateBearing("N", azimuth, "E");
        }

        if (azimuth <= 180)
        {
            return CreateBearing("S", 180 - azimuth, "E");
        }

        if (azimuth <= 270)
        {
            return CreateBearing("S", azimuth - 180, "W");
        }

        return CreateBearing("N", 360 - azimuth, "W");
    }

    public static double DeltasToAzimuth(double deltaX, double deltaY) =>
        NormalizeAzimuth(ToDegrees(Math.Atan2(deltaX, deltaY)));

    public static double NormalizeAzimuth(double azimuth)
    {
        if (double.IsNaN(azimuth) || double.IsInfinity(azimuth))
        {
            throw new SurveyToolException(ErrorCode.InvalidRequest, "azimuth_degrees must be a finite number.");
        }

        var normalized = azimuth % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    public static double ToRadians(double degrees) => degrees * Math.PI / 180d;

    private static double ToDegrees(double radians) => radians * 180d / Math.PI;

    private static SurveyBearingInfo CreateBearing(string northSouth, double angle, string eastWest) =>
        new()
        {
            NorthSouth = northSouth,
            AngleDegrees = angle,
            EastWest = eastWest,
            Text = $"{northSouth} {FormatAngle(angle)} {eastWest}"
        };

    private static string FormatAngle(double angle) =>
        angle.ToString("0.########", CultureInfo.InvariantCulture);
}

public sealed class SurveyToolException : ToolExecutionException
{
    public SurveyToolException(ErrorCode code, string message, JsonObject? details = null)
        : base(code, message, details)
    {
    }
}
