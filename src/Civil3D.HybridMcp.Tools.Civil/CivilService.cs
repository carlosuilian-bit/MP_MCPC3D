using System.Diagnostics;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Tools.Civil;

public sealed class CivilReadService
{
    public AlignmentListResponse ListAlignments(ICivilReadAdapter adapter, DocumentDescriptor descriptor, CivilObjectListRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        var alignments = adapter.ListAlignments()
            .Where(item => CivilFilters.MatchesName(item.Name, request.NameContains))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AlignmentListResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Count = alignments.Length,
            Alignments = alignments
        };
    }

    public AlignmentGetResponse GetAlignment(ICivilReadAdapter adapter, DocumentDescriptor descriptor, CivilObjectGetRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        CivilRequestValidator.ValidateNamedLocator(request);

        var alignment = adapter.GetAlignment(CivilObjectLocator.From(request));
        if (alignment is null)
        {
            throw new CivilToolException(ErrorCode.AlignmentNotFound, "Alignment was not found in the target drawing.");
        }

        return new AlignmentGetResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Alignment = alignment
        };
    }

    public AlignmentStationOffsetResponse AlignmentStationOffset(ICivilReadAdapter adapter, DocumentDescriptor descriptor, AlignmentStationOffsetRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        CivilRequestValidator.ValidateNamedLocator(request.Name, request.Handle, request.ObjectReference, "alignment_station_offset requires name, handle, or object_reference.");

        var result = adapter.GetStationOffset(CivilObjectLocator.From(request), request.X, request.Y);
        return new AlignmentStationOffsetResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Alignment = result.Alignment,
            X = request.X,
            Y = request.Y,
            Station = result.Station,
            Offset = result.Offset
        };
    }

    public AlignmentPointLocationResponse AlignmentPointLocation(ICivilReadAdapter adapter, DocumentDescriptor descriptor, AlignmentPointLocationRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        CivilRequestValidator.ValidateNamedLocator(request.Name, request.Handle, request.ObjectReference, "alignment_point_location requires name, handle, or object_reference.");

        var result = adapter.GetPointLocation(CivilObjectLocator.From(request), request.Station, request.Offset);
        return new AlignmentPointLocationResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Alignment = result.Alignment,
            Station = request.Station,
            Offset = request.Offset,
            X = result.X,
            Y = result.Y
        };
    }

    public SurfaceListResponse ListSurfaces(ICivilReadAdapter adapter, DocumentDescriptor descriptor, CivilObjectListRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        var surfaces = adapter.ListSurfaces()
            .Where(item => CivilFilters.MatchesName(item.Name, request.NameContains))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SurfaceListResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Count = surfaces.Length,
            Surfaces = surfaces
        };
    }

    public SurfaceGetResponse GetSurface(ICivilReadAdapter adapter, DocumentDescriptor descriptor, CivilObjectGetRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        CivilRequestValidator.ValidateNamedLocator(request);

        var surface = adapter.GetSurface(CivilObjectLocator.From(request));
        if (surface is null)
        {
            throw new CivilToolException(ErrorCode.SurfaceNotFound, "Surface was not found in the target drawing.");
        }

        return new SurfaceGetResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Surface = surface
        };
    }

    public SurfaceElevationAtXyResponse SurfaceElevationAtXy(ICivilReadAdapter adapter, DocumentDescriptor descriptor, SurfaceElevationAtXyRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        CivilRequestValidator.ValidateNamedLocator(request.Name, request.Handle, request.ObjectReference, "surface_elevation_at_xy requires name, handle, or object_reference.");

        var result = adapter.GetSurfaceElevationAtXy(CivilObjectLocator.From(request), request.X, request.Y);
        return new SurfaceElevationAtXyResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Surface = result.Surface,
            X = request.X,
            Y = request.Y,
            Elevation = result.Elevation
        };
    }

    public CogoListResponse ListCogoPoints(ICivilReadAdapter adapter, DocumentDescriptor descriptor, CogoListRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        CivilRequestValidator.ValidateCogoList(request);
        var points = adapter.ListCogoPoints()
            .Where(point => CivilFilters.MatchesName(point.RawDescription, request.RawDescriptionContains))
            .Where(point => CivilFilters.MatchesName(point.FullDescription, request.FullDescriptionContains))
            .OrderBy(point => point.PointNumber)
            .ToArray();

        if (request.Limit is { } limit)
        {
            points = points.Take(limit).ToArray();
        }

        return new CogoListResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Count = points.Length,
            Points = points
        };
    }

    /// <summary>
    /// Finds objects carrying a named property set, optionally narrowed by
    /// value.
    ///
    /// The sweep itself belongs to the adapter; what lives here is the part that
    /// has to be right: a filter that constrains nothing is rejected rather than
    /// quietly returning everything, an object missing the filtered property
    /// does not match, and the caller is told both how many objects were looked
    /// at and whether the answer is a prefix.
    /// </summary>
    public PropertySetFindResponse FindPropertySets(
        IPropertySetSweepAdapter adapter,
        DocumentDescriptor descriptor,
        PropertySetFindRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);

        if (string.IsNullOrWhiteSpace(request.SetName))
        {
            throw new CivilToolException(ErrorCode.InvalidRequest, "propertyset_find requires set_name.");
        }

        foreach (var filter in request.Match)
        {
            if (string.IsNullOrWhiteSpace(filter.Name))
            {
                throw new CivilToolException(ErrorCode.InvalidRequest, "Every match filter requires a property name.");
            }

            if (filter.EqualsValue is null && filter.Contains is null)
            {
                throw new CivilToolException(
                    ErrorCode.InvalidRequest,
                    $"Filter on '{filter.Name}' needs equals or contains; a filter that constrains nothing would widen the result.");
            }
        }

        if (request.MaxScan is { } budget && budget <= 0)
        {
            throw new CivilToolException(ErrorCode.InvalidRequest, "max_scan must be greater than zero.");
        }

        var sweep = adapter.SweepPropertySets(request.SetName.Trim(), request.MaxScan);
        var matched = sweep.Carriers
            .Where(carrier => request.Match.All(filter => Matches(carrier.PropertySet, filter)))
            .ToArray();

        var limited = request.Limit is { } limit && limit >= 0
            ? matched.Take(limit).ToArray()
            : matched;

        return new PropertySetFindResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            SetName = request.SetName.Trim(),
            Count = limited.Length,
            MatchedCount = matched.Length,
            ScannedCount = sweep.ScannedCount,
            Truncated = sweep.Truncated || limited.Length < matched.Length,
            Objects = limited
                .Select(carrier => new PropertySetMatch
                {
                    Handle = carrier.Handle,
                    ObjectClass = carrier.ObjectClass,
                    PropertySet = carrier.PropertySet
                })
                .ToArray()
        };
    }

    private static bool Matches(PropertySetInfo propertySet, PropertySetValueFilter filter)
    {
        var property = propertySet.Properties
            .FirstOrDefault(item => string.Equals(item.Name, filter.Name, StringComparison.OrdinalIgnoreCase));

        // An object that does not carry the filtered property is not a match.
        // Treating it as one would return objects the caller did not ask for.
        if (property?.Value is not { } value)
        {
            return false;
        }

        if (filter.EqualsValue is { } exact && !string.Equals(value, exact, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return filter.Contains is not { } fragment ||
               value.Contains(fragment, StringComparison.OrdinalIgnoreCase);
    }

    public PropertySetDefinitionListResponse ListPropertySetDefinitions(ICivilReadAdapter adapter, DocumentDescriptor descriptor, PropertySetDefinitionListRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        var definitions = adapter.ListPropertySetDefinitions()
            .Where(item => CivilFilters.MatchesName(item.Name, request.NameContains))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PropertySetDefinitionListResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Count = definitions.Length,
            Definitions = definitions
        };
    }

    public PropertySetListResponse ListPropertySets(ICivilReadAdapter adapter, DocumentDescriptor descriptor, PropertySetListRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        var handle = ResolveObjectHandle(request.Handle, request.ObjectReference, "propertyset_list");
        var sets = ReadPropertySets(adapter, handle)
            .Where(item => CivilFilters.MatchesName(item.Name, request.NameContains))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PropertySetListResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            ObjectHandle = handle,
            Count = sets.Length,
            PropertySets = sets
        };
    }

    public PropertySetGetResponse GetPropertySet(ICivilReadAdapter adapter, DocumentDescriptor descriptor, PropertySetGetRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new CivilToolException(ErrorCode.InvalidRequest, "propertyset_get requires name.");
        }

        var handle = ResolveObjectHandle(request.Handle, request.ObjectReference, "propertyset_get");
        var sets = ReadPropertySets(adapter, handle);
        var match = sets.FirstOrDefault(item => string.Equals(item.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            throw new CivilToolException(
                ErrorCode.PropertySetNotFound,
                $"Property set '{request.Name}' is not attached to object '{handle}'.");
        }

        return new PropertySetGetResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            ObjectHandle = handle,
            PropertySet = match
        };
    }

    public PropertySetNumericTestCreateResponse CreatePropertySetNumericTest(ICivilWriteAdapter adapter, DocumentDescriptor descriptor, PropertySetNumericTestCreateRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        CivilRequestValidator.ValidatePropertySetNumericTestCreate(request);

        var targetKind = NormalizeTargetKind(request.TargetKind);
        var result = adapter.CreatePropertySetNumericTest(request with
        {
            DefinitionName = request.DefinitionName.Trim(),
            PropertyName = request.PropertyName.Trim(),
            TargetKind = targetKind
        });

        return new PropertySetNumericTestCreateResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            DefinitionName = result.DefinitionName,
            DefinitionHandle = result.DefinitionHandle,
            ObjectHandle = result.ObjectHandle,
            TargetKind = result.TargetKind,
            PropertySet = result.PropertySet
        };
    }

    private static IReadOnlyList<PropertySetInfo> ReadPropertySets(ICivilReadAdapter adapter, string handle) =>
        adapter.ListPropertySets(handle)
            ?? throw new CivilToolException(
                ErrorCode.ObjectNotFound,
                $"Object with handle '{handle}' was not found in the target drawing.");

    private static string ResolveObjectHandle(string? handle, CadObjectReference? reference, string toolName)
    {
        var resolved = !string.IsNullOrWhiteSpace(handle) ? handle : reference?.Handle;
        if (string.IsNullOrWhiteSpace(resolved))
        {
            throw new CivilToolException(
                ErrorCode.InvalidRequest,
                $"{toolName} requires handle or object_reference with a handle.");
        }

        return resolved.Trim();
    }

    private static string NormalizeTargetKind(string? targetKind) =>
        string.IsNullOrWhiteSpace(targetKind) ? "db_point" : targetKind.Trim().ToLowerInvariant();

    public CorridorListResponse ListCorridors(ICivilReadAdapter adapter, DocumentDescriptor descriptor, CivilObjectListRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        var corridors = adapter.ListCorridors()
            .Where(item => CivilFilters.MatchesName(item.Name, request.NameContains))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CorridorListResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Count = corridors.Length,
            Corridors = corridors
        };
    }

    public CorridorGetResponse GetCorridor(ICivilReadAdapter adapter, DocumentDescriptor descriptor, CivilObjectGetRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        CivilRequestValidator.ValidateNamedLocator(request.Name, request.Handle, request.ObjectReference, "corridor_get requires name, handle, or object_reference.");

        var corridor = adapter.GetCorridor(CivilObjectLocator.From(request));
        if (corridor is null)
        {
            throw new CivilToolException(ErrorCode.CorridorNotFound, "Corridor was not found in the target drawing.");
        }

        return new CorridorGetResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Corridor = corridor
        };
    }

    public ProfileListResponse ListProfiles(ICivilReadAdapter adapter, DocumentDescriptor descriptor, ProfileListRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        var profiles = FilterProfiles(adapter.ListProfiles(), request.AlignmentName, request.NameContains);

        return new ProfileListResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Count = profiles.Length,
            Profiles = profiles
        };
    }

    public ProfileGetResponse GetProfile(ICivilReadAdapter adapter, DocumentDescriptor descriptor, ProfileGetRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        CivilRequestValidator.ValidateNamedLocator(request.Name, request.Handle, request.ObjectReference, "profile_get requires name, handle, or object_reference.");

        var profile = ResolveProfile(adapter, ProfileLocator.From(request));
        return new ProfileGetResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Profile = profile
        };
    }

    public ProfileElevationAtStationResponse ProfileElevationAtStation(ICivilReadAdapter adapter, DocumentDescriptor descriptor, ProfileElevationAtStationRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        CivilRequestValidator.ValidateNamedLocator(request.Name, request.Handle, request.ObjectReference, "profile_elevation_at_station requires name, handle, or object_reference.");
        CivilRequestValidator.ValidateFiniteStation(request.Station);

        var profile = ResolveProfile(adapter, ProfileLocator.From(request));
        if (string.IsNullOrWhiteSpace(profile.Handle))
        {
            throw new CivilToolException(ErrorCode.CivilQueryFailed, "Resolved profile does not expose a handle, so its elevation cannot be read.");
        }

        var values = adapter.GetProfileStationValues(profile.Handle, request.Station);
        return new ProfileElevationAtStationResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Profile = profile,
            Station = request.Station,
            Elevation = values.Elevation,
            Grade = values.Grade
        };
    }

    private static ProfileInfo[] FilterProfiles(IReadOnlyList<ProfileInfo> profiles, string? alignmentName, string? nameContains) =>
        profiles
            .Where(item => string.IsNullOrWhiteSpace(alignmentName) ||
                string.Equals(item.AlignmentName, alignmentName, StringComparison.OrdinalIgnoreCase))
            .Where(item => CivilFilters.MatchesName(item.Name, nameContains))
            .OrderBy(item => item.AlignmentName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static ProfileInfo ResolveProfile(ICivilReadAdapter adapter, ProfileLocator locator)
    {
        var matches = adapter.ListProfiles()
            .Where(item => MatchesProfileLocator(item, locator))
            .OrderBy(item => item.AlignmentName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (matches.Length == 0)
        {
            throw new CivilToolException(ErrorCode.ProfileNotFound, "Profile was not found in the target drawing.");
        }

        if (matches.Length > 1)
        {
            var alignments = string.Join(", ", matches.Select(item => item.AlignmentName ?? "<unknown alignment>").Distinct(StringComparer.OrdinalIgnoreCase));
            throw new CivilToolException(
                ErrorCode.ProfileAmbiguous,
                $"Profile name matches {matches.Length} profiles across alignments: {alignments}. Pass alignment_name or handle to disambiguate.");
        }

        return matches[0];
    }

    private static bool MatchesProfileLocator(ProfileInfo profile, ProfileLocator locator)
    {
        if (!string.IsNullOrWhiteSpace(locator.Handle))
        {
            return string.Equals(profile.Handle, locator.Handle, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.Equals(profile.Name, locator.Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(locator.AlignmentName) ||
            string.Equals(profile.AlignmentName, locator.AlignmentName, StringComparison.OrdinalIgnoreCase);
    }

    public CogoGetResponse GetCogoPoint(ICivilReadAdapter adapter, DocumentDescriptor descriptor, CogoGetRequest request)
    {
        CivilRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        CivilRequestValidator.ValidateCogoLocator(request);

        var point = adapter.GetCogoPoint(CogoPointLocator.From(request));
        if (point is null)
        {
            throw new CivilToolException(ErrorCode.CogoPointNotFound, "COGO point was not found in the target drawing.");
        }

        return new CogoGetResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Point = point
        };
    }
}

public sealed class CivilToolHandler : IToolHandler
{
    private readonly ToolDescriptor _descriptor;
    private readonly string _instanceId;
    private readonly ICivilDocumentResolver _documents;
    private readonly IHostApplicationDispatcher _dispatcher;
    private readonly CivilReadService _civil;
    private readonly IConfirmationTokenService _confirmationTokens;
    private readonly IArgumentsHasher _argumentsHasher;

    public CivilToolHandler(
        ToolDescriptor descriptor,
        string instanceId,
        ICivilDocumentResolver documents,
        IHostApplicationDispatcher dispatcher,
        CivilReadService civil,
        IConfirmationTokenService? confirmationTokens = null,
        IArgumentsHasher? argumentsHasher = null)
    {
        _descriptor = descriptor;
        _instanceId = instanceId;
        _documents = documents;
        _dispatcher = dispatcher;
        _civil = civil;
        _confirmationTokens = confirmationTokens ?? new HmacConfirmationTokenService();
        _argumentsHasher = argumentsHasher ?? new CanonicalJsonArgumentsHasher();
    }

    public ToolDescriptor Descriptor => _descriptor;

    public async Task<ToolResponse> ExecuteAsync(ToolRequest request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await _dispatcher.ExecuteAsync(ct => ExecuteOnAutodeskContext(request, ct), cancellationToken);
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
        catch (CivilToolException ex)
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

    private ToolExecutionResult ExecuteOnAutodeskContext(ToolRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return request.ToolName.ToLowerInvariant() switch
        {
            CivilToolNames.AlignmentList => ExecuteAlignmentList(request),
            CivilToolNames.AlignmentGet => ExecuteAlignmentGet(request),
            CivilToolNames.AlignmentStationOffset => ExecuteAlignmentStationOffset(request),
            CivilToolNames.AlignmentPointLocation => ExecuteAlignmentPointLocation(request),
            CivilToolNames.SurfaceList => ExecuteSurfaceList(request),
            CivilToolNames.SurfaceGet => ExecuteSurfaceGet(request),
            CivilToolNames.SurfaceElevationAtXy => ExecuteSurfaceElevationAtXy(request),
            CivilToolNames.CogoList => ExecuteCogoList(request),
            CivilToolNames.CogoGet => ExecuteCogoGet(request),
            CivilToolNames.ProfileList => ExecuteProfileList(request),
            CivilToolNames.ProfileGet => ExecuteProfileGet(request),
            CivilToolNames.ProfileElevationAtStation => ExecuteProfileElevationAtStation(request),
            CivilToolNames.CorridorList => ExecuteCorridorList(request),
            CivilToolNames.CorridorGet => ExecuteCorridorGet(request),
            CivilToolNames.PropertySetDefinitionList => ExecutePropertySetDefinitionList(request),
            CivilToolNames.PropertySetList => ExecutePropertySetList(request),
            CivilToolNames.PropertySetGet => ExecutePropertySetGet(request),
            CivilToolNames.PropertySetNumericTestCreate => ExecutePropertySetNumericTestCreate(request),
            CivilToolNames.PropertySetSet => ExecutePropertySetSet(request),
            CivilToolNames.PropertySetFind => ExecutePropertySetFind(request),
            _ => throw new CivilToolException(ErrorCode.ToolNotFound, $"Tool '{request.ToolName}' is not registered by the Civil 3D handler.")
        };
    }

    private ToolExecutionResult ExecuteAlignmentList(ToolRequest request)
    {
        var input = ReadArguments<CivilObjectListRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var data = _civil.ListAlignments(new AutodeskCivilReadAdapter(target.Document, target.Descriptor), target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteAlignmentGet(ToolRequest request)
    {
        var input = ReadArguments<CivilObjectGetRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var data = _civil.GetAlignment(new AutodeskCivilReadAdapter(target.Document, target.Descriptor), target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteAlignmentStationOffset(ToolRequest request)
    {
        var input = ReadArguments<AlignmentStationOffsetRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var data = _civil.AlignmentStationOffset(new AutodeskCivilReadAdapter(target.Document, target.Descriptor), target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteAlignmentPointLocation(ToolRequest request)
    {
        var input = ReadArguments<AlignmentPointLocationRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var data = _civil.AlignmentPointLocation(new AutodeskCivilReadAdapter(target.Document, target.Descriptor), target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteSurfaceList(ToolRequest request)
    {
        var input = ReadArguments<CivilObjectListRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var data = _civil.ListSurfaces(new AutodeskCivilReadAdapter(target.Document, target.Descriptor), target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteSurfaceGet(ToolRequest request)
    {
        var input = ReadArguments<CivilObjectGetRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var data = _civil.GetSurface(new AutodeskCivilReadAdapter(target.Document, target.Descriptor), target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteSurfaceElevationAtXy(ToolRequest request)
    {
        var input = ReadArguments<SurfaceElevationAtXyRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var data = _civil.SurfaceElevationAtXy(new AutodeskCivilReadAdapter(target.Document, target.Descriptor), target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteCogoList(ToolRequest request)
    {
        var input = ReadArguments<CogoListRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var data = _civil.ListCogoPoints(new AutodeskCivilReadAdapter(target.Document, target.Descriptor), target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteCogoGet(ToolRequest request)
    {
        var input = ReadArguments<CogoGetRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var data = _civil.GetCogoPoint(new AutodeskCivilReadAdapter(target.Document, target.Descriptor), target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecutePropertySetDefinitionList(ToolRequest request)
    {
        var input = ReadArguments<PropertySetDefinitionListRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var data = _civil.ListPropertySetDefinitions(new AutodeskCivilReadAdapter(target.Document, target.Descriptor), target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecutePropertySetList(ToolRequest request)
    {
        var input = ReadArguments<PropertySetListRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var data = _civil.ListPropertySets(new AutodeskCivilReadAdapter(target.Document, target.Descriptor), target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecutePropertySetGet(ToolRequest request)
    {
        var input = ReadArguments<PropertySetGetRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var data = _civil.GetPropertySet(new AutodeskCivilReadAdapter(target.Document, target.Descriptor), target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecutePropertySetNumericTestCreate(ToolRequest request)
    {
        var input = ReadArguments<PropertySetNumericTestCreateRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var pipeline = new CivilWritePipeline<AutodeskCivilWriteContext>(new AutodeskCivilWriteScopeFactory(target.Document));
        var result = pipeline.Execute(context =>
        {
            var adapter = new AutodeskCivilWriteAdapter(target.Document, target.Descriptor, context.Transaction);
            return _civil.CreatePropertySetNumericTest(adapter, target.Descriptor, input);
        });

        var data = result.Data with { Transaction = result.Transaction };
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecutePropertySetFind(ToolRequest request)
    {
        var input = ReadArguments<PropertySetFindRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var adapter = new AutodeskCivilReadAdapter(target.Document, target.Descriptor);
        var data = _civil.FindPropertySets(adapter, target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecutePropertySetSet(ToolRequest request)
    {
        var input = ReadArguments<PropertySetUpdateRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);

        // The gate hashes the request body, never the token, so a confirmation
        // can never be part of what it is confirming.
        var gate = new HmacConfirmationGate(_confirmationTokens, _argumentsHasher, request.Arguments);
        var service = new PropertySetWriteService(gate);
        var context = new PropertySetWriteContext(request.CorrelationId, request.ConfirmationToken);

        var pipeline = new CivilWritePipeline<AutodeskCivilWriteContext>(new AutodeskCivilWriteScopeFactory(target.Document));
        var result = pipeline.Execute(scope =>
        {
            var adapter = new AutodeskPropertySetUpdateAdapter(target.Document, scope.Transaction);
            return service.SetPropertySet(adapter, target.Descriptor, input, context);
        });

        var data = result.Data with { Transaction = result.Transaction };
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteCorridorList(ToolRequest request)
    {
        var input = ReadArguments<CivilObjectListRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var data = _civil.ListCorridors(new AutodeskCivilReadAdapter(target.Document, target.Descriptor), target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteCorridorGet(ToolRequest request)
    {
        var input = ReadArguments<CivilObjectGetRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var data = _civil.GetCorridor(new AutodeskCivilReadAdapter(target.Document, target.Descriptor), target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteProfileList(ToolRequest request)
    {
        var input = ReadArguments<ProfileListRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var data = _civil.ListProfiles(new AutodeskCivilReadAdapter(target.Document, target.Descriptor), target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteProfileGet(ToolRequest request)
    {
        var input = ReadArguments<ProfileGetRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var data = _civil.GetProfile(new AutodeskCivilReadAdapter(target.Document, target.Descriptor), target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteProfileElevationAtStation(ToolRequest request)
    {
        var input = ReadArguments<ProfileElevationAtStationRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var data = _civil.ProfileElevationAtStation(new AutodeskCivilReadAdapter(target.Document, target.Descriptor), target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private void EnsureInstance(string requestedInstanceId)
    {
        if (!string.Equals(requestedInstanceId, _instanceId, StringComparison.Ordinal))
        {
            throw new CivilToolException(ErrorCode.InstanceNotFound, "Civil 3D request instance_id does not match this bridge.");
        }
    }

    private static T ReadArguments<T>(ToolRequest request)
    {
        if (request.Arguments is null)
        {
            throw new CivilToolException(ErrorCode.InvalidRequest, "Civil 3D tool request requires structured arguments.");
        }

        return request.Arguments.Deserialize<T>(JsonContractOptions.Default)
            ?? throw new CivilToolException(ErrorCode.InvalidRequest, "Civil 3D tool request arguments could not be read.");
    }

    private static ToolResponse Error(
        ToolRequest request,
        ErrorCode code,
        string message,
        long started,
        System.Text.Json.Nodes.JsonObject? details = null) =>
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

    private sealed record ToolExecutionResult(DocumentDescriptor Document, object Data);
}

public interface ICivilDocumentResolver
{
    ResolvedCivilDocument ResolveCivilDocument(string? documentId);
}

public sealed record ResolvedCivilDocument(Document Document, DocumentDescriptor Descriptor);

public interface ICivilReadAdapter
{
    IReadOnlyList<AlignmentInfo> ListAlignments();
    AlignmentInfo? GetAlignment(CivilObjectLocator locator);
    AlignmentStationOffsetResult GetStationOffset(CivilObjectLocator locator, double x, double y);
    AlignmentPointLocationResult GetPointLocation(CivilObjectLocator locator, double station, double offset);
    IReadOnlyList<SurfaceInfo> ListSurfaces();
    SurfaceInfo? GetSurface(CivilObjectLocator locator);
    SurfaceElevationAtXyResult GetSurfaceElevationAtXy(CivilObjectLocator locator, double x, double y);
    IReadOnlyList<CogoPointInfo> ListCogoPoints();
    CogoPointInfo? GetCogoPoint(CogoPointLocator locator);
    IReadOnlyList<ProfileInfo> ListProfiles();
    ProfileStationResult GetProfileStationValues(string profileHandle, double station);
    IReadOnlyList<CorridorInfo> ListCorridors();
    CorridorInfo? GetCorridor(CivilObjectLocator locator);
    IReadOnlyList<PropertySetDefinitionInfo> ListPropertySetDefinitions();

    /// <summary>Returns null when the object handle does not resolve in the drawing.</summary>
    IReadOnlyList<PropertySetInfo>? ListPropertySets(string objectHandle);
}

public interface ICivilWriteAdapter
{
    PropertySetNumericTestResult CreatePropertySetNumericTest(PropertySetNumericTestCreateRequest request);
}

public sealed record PropertySetNumericTestResult(
    string DefinitionName,
    string? DefinitionHandle,
    string ObjectHandle,
    string TargetKind,
    PropertySetInfo PropertySet);

/// <param name="Handle">Handle of the object carrying the set.</param>
public sealed record PropertySetCarrier(string Handle, string? ObjectClass, PropertySetInfo PropertySet);

/// <param name="ScannedCount">How many objects the sweep looked at.</param>
/// <param name="Truncated">True when the sweep stopped before the end of the drawing.</param>
public sealed record PropertySetSweep(IReadOnlyList<PropertySetCarrier> Carriers, int ScannedCount, bool Truncated);

/// <summary>
/// Sweeps the drawing for objects carrying one named property set. Separate from
/// the read adapter because it is the only property-set operation that does not
/// start from a handle.
/// </summary>
public interface IPropertySetSweepAdapter
{
    /// <param name="maxScan">Caller's scan budget, or null for the adapter default.</param>
    PropertySetSweep SweepPropertySets(string setName, int? maxScan);
}

public sealed record CivilObjectLocator(string? Name, string? Handle)
{
    public static CivilObjectLocator From(CivilObjectGetRequest request) =>
        new(FirstNonBlank(request.Name, request.ObjectReference?.Name), FirstNonBlank(request.Handle, request.ObjectReference?.Handle));

    public static CivilObjectLocator From(AlignmentStationOffsetRequest request) =>
        new(FirstNonBlank(request.Name, request.ObjectReference?.Name), FirstNonBlank(request.Handle, request.ObjectReference?.Handle));

    public static CivilObjectLocator From(AlignmentPointLocationRequest request) =>
        new(FirstNonBlank(request.Name, request.ObjectReference?.Name), FirstNonBlank(request.Handle, request.ObjectReference?.Handle));

    public static CivilObjectLocator From(SurfaceElevationAtXyRequest request) =>
        new(FirstNonBlank(request.Name, request.ObjectReference?.Name), FirstNonBlank(request.Handle, request.ObjectReference?.Handle));

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public sealed record CogoPointLocator(long? PointNumber, string? Handle)
{
    public static CogoPointLocator From(CogoGetRequest request) =>
        new(request.PointNumber, FirstNonBlank(request.Handle, request.ObjectReference?.Handle));

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public sealed record ProfileLocator(string? Name, string? Handle, string? AlignmentName)
{
    public static ProfileLocator From(ProfileGetRequest request) =>
        new(
            FirstNonBlank(request.Name, request.ObjectReference?.Name),
            FirstNonBlank(request.Handle, request.ObjectReference?.Handle),
            request.AlignmentName);

    public static ProfileLocator From(ProfileElevationAtStationRequest request) =>
        new(
            FirstNonBlank(request.Name, request.ObjectReference?.Name),
            FirstNonBlank(request.Handle, request.ObjectReference?.Handle),
            request.AlignmentName);

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public sealed record ProfileStationResult(double? Elevation, double? Grade);

public sealed record AlignmentStationOffsetResult(AlignmentInfo Alignment, double Station, double Offset);
public sealed record AlignmentPointLocationResult(AlignmentInfo Alignment, double X, double Y);
public sealed record SurfaceElevationAtXyResult(SurfaceInfo Surface, double Elevation);

public static class CivilFilters
{
    public static bool MatchesName(string? value, string? contains) =>
        string.IsNullOrWhiteSpace(contains) ||
        (!string.IsNullOrWhiteSpace(value) && value.Contains(contains, StringComparison.OrdinalIgnoreCase));
}

public static class CivilRequestValidator
{
    public static void ValidateDocumentTarget(string instanceId, string documentId, DocumentDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new CivilToolException(ErrorCode.InvalidRequest, "instance_id is required.");
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            throw new CivilToolException(ErrorCode.DocumentAmbiguous, "document_id is required for Civil 3D tools.");
        }

        if (!string.Equals(instanceId, descriptor.InstanceId, StringComparison.Ordinal) ||
            !string.Equals(documentId, descriptor.DocumentId, StringComparison.Ordinal))
        {
            throw new CivilToolException(ErrorCode.DocumentNotFound, "Target drawing document was not found in this Civil 3D instance.");
        }
    }

    public static void ValidateNamedLocator(CivilObjectGetRequest request) =>
        ValidateNamedLocator(request.Name, request.Handle, request.ObjectReference, "Tool requires name, handle, or object_reference with name/handle.");

    public static void ValidateNamedLocator(string? name, string? handle, CadObjectReference? reference, string message)
    {
        var hasName = !string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(reference?.Name);
        var hasHandle = !string.IsNullOrWhiteSpace(handle) || !string.IsNullOrWhiteSpace(reference?.Handle);
        if (!hasName && !hasHandle)
        {
            throw new CivilToolException(ErrorCode.InvalidRequest, message);
        }
    }

    public static void ValidateFiniteStation(double station)
    {
        if (double.IsNaN(station) || double.IsInfinity(station))
        {
            throw new CivilToolException(ErrorCode.InvalidRequest, "station must be a finite number.");
        }
    }

    public static void ValidateCogoList(CogoListRequest request)
    {
        if (request.Limit is <= 0)
        {
            throw new CivilToolException(ErrorCode.InvalidRequest, "limit must be greater than zero when provided.");
        }
    }

    public static void ValidateCogoLocator(CogoGetRequest request)
    {
        var hasPointNumber = request.PointNumber is not null;
        var hasHandle = !string.IsNullOrWhiteSpace(request.Handle) || !string.IsNullOrWhiteSpace(request.ObjectReference?.Handle);
        if (!hasPointNumber && !hasHandle)
        {
            throw new CivilToolException(ErrorCode.InvalidRequest, "cogo_get requires point_number, handle, or object_reference with handle.");
        }
    }

    public static void ValidatePropertySetNumericTestCreate(PropertySetNumericTestCreateRequest request)
    {
        ValidateMcpTestName(request.DefinitionName, "definition_name");
        ValidateNonEmptyName(request.PropertyName, "property_name");

        if (!request.DefinitionName.Trim().StartsWith("MCP_NUMERIC_TEST", StringComparison.OrdinalIgnoreCase))
        {
            throw new CivilToolException(ErrorCode.InvalidRequest, "definition_name must start with MCP_NUMERIC_TEST.");
        }

        if (double.IsNaN(request.NumericValue) || double.IsInfinity(request.NumericValue))
        {
            throw new CivilToolException(ErrorCode.InvalidRequest, "numeric_value must be a finite number.");
        }

        if (!string.IsNullOrWhiteSpace(request.TargetKind) &&
            !string.Equals(request.TargetKind.Trim(), "db_point", StringComparison.OrdinalIgnoreCase))
        {
            throw new CivilToolException(ErrorCode.InvalidRequest, "target_kind must be db_point when provided.");
        }
    }

    private static void ValidateMcpTestName(string value, string fieldName)
    {
        ValidateNonEmptyName(value, fieldName);
        if (value.Any(ch => ch < 32 || "<>/\\\":;?*|=,".Contains(ch)))
        {
            throw new CivilToolException(ErrorCode.InvalidRequest, $"{fieldName} contains characters that are not allowed for this test tool.");
        }
    }

    private static void ValidateNonEmptyName(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CivilToolException(ErrorCode.InvalidRequest, $"{fieldName} is required.");
        }
    }
}

public sealed class CivilToolException : Exception
{
    public CivilToolException(ErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public CivilToolException(ErrorCode code, string message, Exception? innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public CivilToolException(ErrorCode code, string message, System.Text.Json.Nodes.JsonObject? details)
        : base(message)
    {
        Code = code;
        Details = details;
    }

    public ErrorCode Code { get; }

    /// <summary>
    /// Structured payload that travels with the error. A confirmation-required
    /// failure uses it to hand back the preview and the token, without which the
    /// caller would have no way to confirm.
    /// </summary>
    public System.Text.Json.Nodes.JsonObject? Details { get; }
}
