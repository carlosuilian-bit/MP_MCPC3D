using System.Diagnostics;
using System.Text.Json;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;
using Civil3D.HybridMcp.Tools.Civil;
using Civil3D.HybridMcp.Tools.Roadway;

namespace Civil3D.HybridMcp.Bridge;

/// <summary>
/// Adapts the Civil 3D read adapter to the narrow port the roadway module needs.
///
/// This is where "roadway tools sit on top of the generic tools" becomes real:
/// the station and offset maths is the same code `alignment_station_offset` uses
/// and that was validated live, not a second implementation that could drift
/// from it.
/// </summary>
public sealed class AutodeskRoadwayAlignmentAdapter : IRoadwayAlignmentAdapter
{
    private readonly AutodeskCivilReadAdapter _civil;

    public AutodeskRoadwayAlignmentAdapter(AutodeskCivilReadAdapter civil)
    {
        _civil = civil;
    }

    public bool AlignmentFound(string alignment) =>
        _civil.GetAlignment(new CivilObjectLocator(alignment, null)) is not null;

    public RoadwayStationOffsetResult StationOffset(string alignment, double x, double y)
    {
        try
        {
            var result = _civil.GetStationOffset(new CivilObjectLocator(alignment, null), x, y);
            return new RoadwayStationOffsetResult(true, result.Station, result.Offset, null);
        }
        catch (CivilToolException ex)
        {
            // A point off the end of the alignment is an ordinary outcome in
            // roadway work, so it becomes a failed row rather than ending the
            // batch the operator is waiting on.
            return new RoadwayStationOffsetResult(false, null, null, ex.Message);
        }
    }
}

/// <summary>
/// Adapts the Civil property-set write adapter to the roadway port, so the batch
/// write reuses the same code `propertyset_set` validated live.
/// </summary>
public sealed class AutodeskRoadwayPropertySetAdapter : IRoadwayPropertySetAdapter
{
    private readonly AutodeskPropertySetUpdateAdapter _inner;

    public AutodeskRoadwayPropertySetAdapter(AutodeskPropertySetUpdateAdapter inner)
    {
        _inner = inner;
    }

    public RoadwayPropertySetState Read(string handle, string setName)
    {
        var state = _inner.Read(handle, setName);
        return new RoadwayPropertySetState(state.ObjectFound, state.PropertySet);
    }

    public IReadOnlyList<PropertyValueInfo> Apply(
        string handle,
        string setName,
        IReadOnlyList<PropertySetValueUpdate> updates) =>
        _inner.Apply(handle, setName, updates);
}

public sealed class RoadwayToolHandler : IToolHandler
{
    private readonly ToolDescriptor _descriptor;
    private readonly string _instanceId;
    private readonly ICivilDocumentResolver _documents;
    private readonly IHostApplicationDispatcher _dispatcher;
    private readonly RoadwayStationOffsetService _roadway = new();

    // One signing service per handler, not per call. A new HmacConfirmationTokenService
    // generates a fresh random secret, so constructing one inside the request
    // would sign the token with a key the confirming call no longer has.
    private readonly IConfirmationTokenService _confirmationTokens = new HmacConfirmationTokenService();
    private readonly IArgumentsHasher _argumentsHasher = new CanonicalJsonArgumentsHasher();

    public RoadwayToolHandler(
        ToolDescriptor descriptor,
        string instanceId,
        ICivilDocumentResolver documents,
        IHostApplicationDispatcher dispatcher)
    {
        _descriptor = descriptor;
        _instanceId = instanceId;
        _documents = documents;
        _dispatcher = dispatcher;
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
        catch (RoadwayToolException ex)
        {
            return Error(request, ex.Code, ex.Message, started, ex.Details);
        }
        catch (CivilToolException ex)
        {
            return Error(request, ex.Code, ex.Message, started);
        }
        catch (JsonException ex)
        {
            return Error(request, ErrorCode.InvalidRequest, ex.Message, started);
        }
        catch (System.Exception ex)
        {
            return Error(request, ErrorCode.InternalError, ex.Message, started);
        }
    }

    private RoadwayExecutionResult ExecuteOnAutodeskContext(ToolRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return request.ToolName.ToLowerInvariant() switch
        {
            RoadwayToolNames.StationOffsetBatch => ExecuteStationOffsetBatch(request),
            RoadwayToolNames.PropertySetWrite => ExecutePropertySetWrite(request),
            _ => throw new RoadwayToolException(ErrorCode.ToolNotFound, $"Tool '{request.ToolName}' is not registered by the roadway handler.")
        };
    }

    private RoadwayExecutionResult ExecuteStationOffsetBatch(ToolRequest request)
    {
        var input = ReadArguments<RoadwayStationOffsetBatchRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);
        var adapter = new AutodeskRoadwayAlignmentAdapter(new AutodeskCivilReadAdapter(target.Document, target.Descriptor));
        var data = _roadway.StationOffsetBatch(adapter, target.Descriptor, input);
        return new RoadwayExecutionResult(target.Descriptor, data);
    }

    private RoadwayExecutionResult ExecutePropertySetWrite(ToolRequest request)
    {
        var input = ReadArguments<RoadwayPropertySetWriteRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.ResolveCivilDocument(input.DocumentId);

        var gate = new HmacConfirmationGate(_confirmationTokens, _argumentsHasher, request.Arguments);
        var service = new RoadwayPropertySetWriteService(gate);
        var context = new RoadwayWriteContext(request.CorrelationId, request.ConfirmationToken);

        var pipeline = new CivilWritePipeline<AutodeskCivilWriteContext>(new AutodeskCivilWriteScopeFactory(target.Document));
        var result = pipeline.Execute(scope =>
        {
            var adapter = new AutodeskRoadwayPropertySetAdapter(
                new AutodeskPropertySetUpdateAdapter(target.Document, scope.Transaction));
            return service.Write(adapter, target.Descriptor, input, context);
        });

        var data = result.Data with { Transaction = result.Transaction };
        return new RoadwayExecutionResult(target.Descriptor, data);
    }

    private void EnsureInstance(string requestedInstanceId)
    {
        if (!string.Equals(requestedInstanceId, _instanceId, StringComparison.Ordinal))
        {
            throw new RoadwayToolException(ErrorCode.InstanceNotFound, "Roadway request instance_id does not match this Civil 3D bridge.");
        }
    }

    private static T ReadArguments<T>(ToolRequest request)
    {
        if (request.Arguments is null)
        {
            throw new RoadwayToolException(ErrorCode.InvalidRequest, "Roadway tool request requires structured arguments.");
        }

        return request.Arguments.Deserialize<T>(JsonContractOptions.Default)
            ?? throw new RoadwayToolException(ErrorCode.InvalidRequest, "Roadway tool request arguments could not be read.");
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

    private sealed record RoadwayExecutionResult(DocumentDescriptor Document, object Data);
}
