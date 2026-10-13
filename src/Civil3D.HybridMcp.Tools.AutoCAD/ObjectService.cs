using System.Diagnostics;
using System.Text.Json;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Tools.AutoCAD;

/// <summary>
/// Pure rules for the generic object reads. Every Autodesk call stays behind
/// <see cref="IObjectReadAdapter"/> so the filtering and failure behaviour can be
/// tested without a running Civil 3D.
/// </summary>
public sealed class ObjectService
{
    public ObjectGetResponse GetObject(IObjectReadAdapter adapter, DocumentDescriptor descriptor, ObjectGetRequest request)
    {
        ObjectRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        var handle = ObjectRequestValidator.RequireHandle(request.Handle, request.ObjectReference?.Handle, AutoCadToolNames.ObjectGet);

        var info = adapter.GetObject(handle)
            ?? throw new ObjectToolException(ErrorCode.ObjectNotFound, $"Handle '{handle}' does not resolve to an object in the target drawing.");

        return new ObjectGetResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Object = info
        };
    }

    public ObjectPropertiesResponse GetObjectProperties(IObjectReadAdapter adapter, DocumentDescriptor descriptor, ObjectPropertiesRequest request)
    {
        ObjectRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        var handle = ObjectRequestValidator.RequireHandle(request.Handle, request.ObjectReference?.Handle, AutoCadToolNames.ObjectProperties);

        var result = adapter.ReadProperties(handle)
            ?? throw new ObjectToolException(ErrorCode.ObjectNotFound, $"Handle '{handle}' does not resolve to an object in the target drawing.");

        var includeUnreadable = request.IncludeUnreadable ?? false;
        var properties = result.Properties
            .Where(property => includeUnreadable || property.Value is not null)
            .Where(property => MatchesNameFilter(property.Name, request.NameContains))
            .ToArray();

        return new ObjectPropertiesResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Handle = handle,
            ObjectClass = result.ObjectClass,
            Count = properties.Length,
            // Truncation is decided by the adapter, which is the only layer that
            // knows how many properties it stopped short of reading.
            Truncated = result.Truncated,
            Properties = properties
        };
    }

    private static bool MatchesNameFilter(string name, string? nameContains) =>
        string.IsNullOrWhiteSpace(nameContains) ||
        name.Contains(nameContains, StringComparison.OrdinalIgnoreCase);
}

public sealed class AutoCadObjectToolHandler : IToolHandler
{
    private readonly ToolDescriptor _descriptor;
    private readonly string _instanceId;
    private readonly IAutoCadDocumentResolver _documents;
    private readonly IHostApplicationDispatcher _dispatcher;
    private readonly ObjectService _objects;

    public AutoCadObjectToolHandler(
        ToolDescriptor descriptor,
        string instanceId,
        IAutoCadDocumentResolver documents,
        IHostApplicationDispatcher dispatcher,
        ObjectService objects)
    {
        _descriptor = descriptor;
        _instanceId = instanceId;
        _documents = documents;
        _dispatcher = dispatcher;
        _objects = objects;
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
        catch (ObjectToolException ex)
        {
            return Error(request, ex.Code, ex.Message, started);
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
            AutoCadToolNames.ObjectGet => ExecuteObjectGet(request),
            AutoCadToolNames.ObjectProperties => ExecuteObjectProperties(request),
            _ => throw new ObjectToolException(ErrorCode.ToolNotFound, $"Tool '{request.ToolName}' is not registered by the AutoCAD object handler.")
        };
    }

    private ToolExecutionResult ExecuteObjectGet(ToolRequest request)
    {
        var input = ReadArguments<ObjectGetRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.Resolve(input.DocumentId);
        var adapter = new AutodeskObjectAdapter(target.Document, target.Descriptor);
        var data = _objects.GetObject(adapter, target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteObjectProperties(ToolRequest request)
    {
        var input = ReadArguments<ObjectPropertiesRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.Resolve(input.DocumentId);
        var adapter = new AutodeskObjectAdapter(target.Document, target.Descriptor);
        var data = _objects.GetObjectProperties(adapter, target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private void EnsureInstance(string requestedInstanceId)
    {
        if (!string.Equals(requestedInstanceId, _instanceId, StringComparison.Ordinal))
        {
            throw new ObjectToolException(ErrorCode.InstanceNotFound, "Object request instance_id does not match this Civil 3D bridge.");
        }
    }

    private static T ReadArguments<T>(ToolRequest request)
    {
        if (request.Arguments is null)
        {
            throw new ObjectToolException(ErrorCode.InvalidRequest, "Object tool request requires structured arguments.");
        }

        return request.Arguments.Deserialize<T>(JsonContractOptions.Default)
            ?? throw new ObjectToolException(ErrorCode.InvalidRequest, "Object tool request arguments could not be read.");
    }

    private static ToolResponse Error(ToolRequest request, ErrorCode code, string message, long started) =>
        new()
        {
            Success = false,
            Tool = request.ToolName,
            CorrelationId = request.CorrelationId,
            Error = new ErrorEnvelope
            {
                Code = code,
                Message = message,
                CorrelationId = request.CorrelationId
            },
            Duration = Stopwatch.GetElapsedTime(started)
        };

    private sealed record ToolExecutionResult(DocumentDescriptor Document, object Data);
}

public interface IObjectReadAdapter
{
    CadObjectInfo? GetObject(string handle);

    ObjectPropertyReadResult? ReadProperties(string handle);
}

/// <param name="ObjectClass">Runtime class of the object the properties were read from.</param>
/// <param name="Properties">Properties in declaration order.</param>
/// <param name="Truncated">True when the adapter stopped before the end of the property list.</param>
public sealed record ObjectPropertyReadResult(
    string? ObjectClass,
    IReadOnlyList<ObjectPropertyInfo> Properties,
    bool Truncated);

public static class ObjectRequestValidator
{
    public static void ValidateDocumentTarget(string instanceId, string documentId, DocumentDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new ObjectToolException(ErrorCode.InvalidRequest, "instance_id is required.");
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            throw new ObjectToolException(ErrorCode.DocumentAmbiguous, "document_id is required for object tools.");
        }

        if (!string.Equals(instanceId, descriptor.InstanceId, StringComparison.Ordinal) ||
            !string.Equals(documentId, descriptor.DocumentId, StringComparison.Ordinal))
        {
            throw new ObjectToolException(ErrorCode.DocumentNotFound, "Target drawing document was not found in this Civil 3D instance.");
        }
    }

    public static string RequireHandle(string? handle, string? referenceHandle, string toolName)
    {
        var resolved = FirstNonBlank(handle, referenceHandle);
        if (resolved is null)
        {
            throw new ObjectToolException(
                ErrorCode.InvalidRequest,
                $"{toolName} requires handle or object_reference.handle.");
        }

        return resolved;
    }

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim();
}

public sealed class ObjectToolException : Exception
{
    public ObjectToolException(ErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public ErrorCode Code { get; }
}
