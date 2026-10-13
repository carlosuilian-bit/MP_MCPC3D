using System.Diagnostics;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Tools.AutoCAD;

public sealed class LayerService
{
    public LayerListResponse ListLayers(ILayerAdapter adapter, DocumentDescriptor descriptor, LayerListRequest request)
    {
        LayerRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        var layers = adapter.ListLayers()
            .Where(layer => LayerFilters.Matches(layer, request))
            .OrderBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new LayerListResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Count = layers.Length,
            Layers = layers
        };
    }

    public LayerGetResponse GetLayer(ILayerAdapter adapter, DocumentDescriptor descriptor, LayerGetRequest request)
    {
        LayerRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        LayerRequestValidator.ValidateLayerLookup(request);

        var locator = LayerLocator.From(request);
        var layer = adapter.GetLayer(locator);
        if (layer is null)
        {
            throw new LayerToolException(ErrorCode.LayerNotFound, "Layer was not found in the target drawing.");
        }

        return new LayerGetResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Layer = layer
        };
    }

    public LayerCreateResponse CreateLayer(ILayerWriteAdapter adapter, DocumentDescriptor descriptor, LayerCreateRequest request)
    {
        LayerRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        LayerRequestValidator.ValidateLayerCreate(request);

        var layer = adapter.CreateLayer(request);
        return new LayerCreateResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Created = true,
            Layer = layer
        };
    }

    public LayerSetCurrentResponse SetCurrentLayer(ILayerWriteAdapter adapter, DocumentDescriptor descriptor, LayerSetCurrentRequest request)
    {
        LayerRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        LayerRequestValidator.ValidateLayerSetCurrent(request);

        var result = adapter.SetCurrentLayer(request);
        return new LayerSetCurrentResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Changed = result.Changed,
            PreviousLayer = result.PreviousLayer,
            CurrentLayer = result.CurrentLayer
        };
    }

    public LayerUpdateResponse UpdateLayer(ILayerWriteAdapter adapter, DocumentDescriptor descriptor, LayerUpdateRequest request)
    {
        LayerRequestValidator.ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);
        LayerRequestValidator.ValidateLayerUpdate(request);

        var result = adapter.UpdateLayer(request);
        return new LayerUpdateResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Changed = result.Changed,
            PreviousLayer = result.PreviousLayer,
            UpdatedLayer = result.UpdatedLayer
        };
    }
}

public sealed class AutoCadLayerToolHandler : IToolHandler
{
    private readonly ToolDescriptor _descriptor;
    private readonly string _instanceId;
    private readonly IAutoCadDocumentResolver _documents;
    private readonly IHostApplicationDispatcher _dispatcher;
    private readonly LayerService _layers;

    public AutoCadLayerToolHandler(
        ToolDescriptor descriptor,
        string instanceId,
        IAutoCadDocumentResolver documents,
        IHostApplicationDispatcher dispatcher,
        LayerService layers)
    {
        _descriptor = descriptor;
        _instanceId = instanceId;
        _documents = documents;
        _dispatcher = dispatcher;
        _layers = layers;
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
        catch (LayerToolException ex)
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
            AutoCadToolNames.LayerList => ExecuteLayerList(request),
            AutoCadToolNames.LayerGet => ExecuteLayerGet(request),
            AutoCadToolNames.LayerCreate => ExecuteLayerCreate(request),
            AutoCadToolNames.LayerUpdate => ExecuteLayerUpdate(request),
            AutoCadToolNames.LayerSetCurrent => ExecuteLayerSetCurrent(request),
            _ => throw new LayerToolException(ErrorCode.ToolNotFound, $"Tool '{request.ToolName}' is not registered by the AutoCAD layer handler.")
        };
    }

    private ToolExecutionResult ExecuteLayerList(ToolRequest request)
    {
        var input = ReadArguments<LayerListRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.Resolve(input.DocumentId);
        var adapter = new AutodeskLayerAdapter(target.Document, target.Descriptor);
        var data = _layers.ListLayers(adapter, target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteLayerGet(ToolRequest request)
    {
        var input = ReadArguments<LayerGetRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.Resolve(input.DocumentId);
        var adapter = new AutodeskLayerAdapter(target.Document, target.Descriptor);
        var data = _layers.GetLayer(adapter, target.Descriptor, input);
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteLayerCreate(ToolRequest request)
    {
        var input = ReadArguments<LayerCreateRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.Resolve(input.DocumentId);
        var pipeline = new WritePipeline<AutodeskWriteContext>(new AutodeskWriteScopeFactory(target.Document));
        var result = pipeline.Execute(context =>
        {
            var adapter = new AutodeskLayerWriteAdapter(target.Document, target.Descriptor, context.Transaction);
            return _layers.CreateLayer(adapter, target.Descriptor, input);
        });

        var data = result.Data with { Transaction = result.Transaction };
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteLayerUpdate(ToolRequest request)
    {
        var input = ReadArguments<LayerUpdateRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.Resolve(input.DocumentId);
        var pipeline = new WritePipeline<AutodeskWriteContext>(new AutodeskWriteScopeFactory(target.Document));
        var result = pipeline.Execute(context =>
        {
            var adapter = new AutodeskLayerWriteAdapter(target.Document, target.Descriptor, context.Transaction);
            return _layers.UpdateLayer(adapter, target.Descriptor, input);
        });

        var data = result.Data with { Transaction = result.Transaction };
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private ToolExecutionResult ExecuteLayerSetCurrent(ToolRequest request)
    {
        var input = ReadArguments<LayerSetCurrentRequest>(request);
        EnsureInstance(input.InstanceId);
        var target = _documents.Resolve(input.DocumentId);
        var pipeline = new WritePipeline<AutodeskWriteContext>(new AutodeskWriteScopeFactory(target.Document));
        var result = pipeline.Execute(context =>
        {
            var adapter = new AutodeskLayerWriteAdapter(target.Document, target.Descriptor, context.Transaction);
            return _layers.SetCurrentLayer(adapter, target.Descriptor, input);
        });

        var data = result.Data with { Transaction = result.Transaction };
        return new ToolExecutionResult(target.Descriptor, data);
    }

    private void EnsureInstance(string requestedInstanceId)
    {
        if (!string.Equals(requestedInstanceId, _instanceId, StringComparison.Ordinal))
        {
            throw new LayerToolException(ErrorCode.InstanceNotFound, "Layer request instance_id does not match this Civil 3D bridge.");
        }
    }

    private static T ReadArguments<T>(ToolRequest request)
    {
        if (request.Arguments is null)
        {
            throw new LayerToolException(ErrorCode.InvalidRequest, "Layer tool request requires structured arguments.");
        }

        return request.Arguments.Deserialize<T>(JsonContractOptions.Default)
            ?? throw new LayerToolException(ErrorCode.InvalidRequest, "Layer tool request arguments could not be read.");
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

public interface IAutoCadDocumentResolver
{
    ResolvedAutoCadDocument Resolve(string? documentId);
}

public sealed record ResolvedAutoCadDocument(Document Document, DocumentDescriptor Descriptor);

public interface ILayerAdapter
{
    IReadOnlyList<LayerInfo> ListLayers();
    LayerInfo? GetLayer(LayerLocator locator);
}

public interface ILayerWriteAdapter
{
    LayerInfo CreateLayer(LayerCreateRequest request);
    LayerSetCurrentResult SetCurrentLayer(LayerSetCurrentRequest request);
    LayerUpdateResult UpdateLayer(LayerUpdateRequest request);
}

public sealed record LayerLocator(string? Name, string? Handle)
{
    public static LayerLocator From(LayerGetRequest request)
    {
        var name = FirstNonBlank(request.Name, request.ObjectReference?.Name);
        var handle = FirstNonBlank(request.Handle, request.ObjectReference?.Handle);
        return new LayerLocator(name, handle);
    }

    public static LayerLocator From(LayerSetCurrentRequest request)
    {
        var name = FirstNonBlank(request.Name, request.ObjectReference?.Name);
        var handle = FirstNonBlank(request.Handle, request.ObjectReference?.Handle);
        return new LayerLocator(name, handle);
    }

    public static LayerLocator From(LayerUpdateRequest request)
    {
        var name = FirstNonBlank(request.Name, request.ObjectReference?.Name);
        var handle = FirstNonBlank(request.Handle, request.ObjectReference?.Handle);
        return new LayerLocator(name, handle);
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

public sealed record LayerSetCurrentResult(LayerInfo PreviousLayer, LayerInfo CurrentLayer, bool Changed);

public sealed record LayerUpdateResult(LayerInfo PreviousLayer, LayerInfo UpdatedLayer, bool Changed);

public static class LayerFilters
{
    public static bool Matches(LayerInfo layer, LayerListRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.NameContains) &&
            !layer.Name.Contains(request.NameContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Matches(request.IsOn, layer.IsOn) &&
            Matches(request.IsFrozen, layer.IsFrozen) &&
            Matches(request.IsLocked, layer.IsLocked) &&
            Matches(request.IsPlottable, layer.IsPlottable);
    }

    private static bool Matches(bool? expected, bool actual) =>
        expected is null || expected.Value == actual;
}

public static class LayerRequestValidator
{
    public static void ValidateDocumentTarget(string instanceId, string documentId, DocumentDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new LayerToolException(ErrorCode.InvalidRequest, "instance_id is required.");
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            throw new LayerToolException(ErrorCode.DocumentAmbiguous, "document_id is required for layer tools.");
        }

        if (!string.Equals(instanceId, descriptor.InstanceId, StringComparison.Ordinal) ||
            !string.Equals(documentId, descriptor.DocumentId, StringComparison.Ordinal))
        {
            throw new LayerToolException(ErrorCode.DocumentNotFound, "Target drawing document was not found in this Civil 3D instance.");
        }
    }

    public static void ValidateLayerLookup(LayerGetRequest request)
    {
        var hasName = !string.IsNullOrWhiteSpace(request.Name) || !string.IsNullOrWhiteSpace(request.ObjectReference?.Name);
        var hasHandle = !string.IsNullOrWhiteSpace(request.Handle) || !string.IsNullOrWhiteSpace(request.ObjectReference?.Handle);

        if (!hasName && !hasHandle)
        {
            throw new LayerToolException(ErrorCode.InvalidRequest, "layer_get requires name, handle, or object_reference with name/handle.");
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            ValidateLayerNameSyntax(request.Name);
        }

        if (!string.IsNullOrWhiteSpace(request.ObjectReference?.Name))
        {
            ValidateLayerNameSyntax(request.ObjectReference.Name);
        }
    }

    public static void ValidateLayerCreate(LayerCreateRequest request)
    {
        ValidateLayerNameSyntax(request.Name);

        if (request.ColorIndex is < 1 or > 255)
        {
            throw new LayerToolException(ErrorCode.InvalidRequest, "color_index must be between 1 and 255 when provided.");
        }
    }

    public static void ValidateLayerSetCurrent(LayerSetCurrentRequest request)
    {
        var hasName = !string.IsNullOrWhiteSpace(request.Name) || !string.IsNullOrWhiteSpace(request.ObjectReference?.Name);
        var hasHandle = !string.IsNullOrWhiteSpace(request.Handle) || !string.IsNullOrWhiteSpace(request.ObjectReference?.Handle);

        if (!hasName && !hasHandle)
        {
            throw new LayerToolException(ErrorCode.InvalidRequest, "layer_set_current requires name, handle, or object_reference with name/handle.");
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            ValidateLayerNameSyntax(request.Name);
        }

        if (!string.IsNullOrWhiteSpace(request.ObjectReference?.Name))
        {
            ValidateLayerNameSyntax(request.ObjectReference.Name);
        }
    }

    public static void ValidateLayerUpdate(LayerUpdateRequest request)
    {
        var hasName = !string.IsNullOrWhiteSpace(request.Name) || !string.IsNullOrWhiteSpace(request.ObjectReference?.Name);
        var hasHandle = !string.IsNullOrWhiteSpace(request.Handle) || !string.IsNullOrWhiteSpace(request.ObjectReference?.Handle);

        if (!hasName && !hasHandle)
        {
            throw new LayerToolException(ErrorCode.InvalidRequest, "layer_update requires name, handle, or object_reference with name/handle.");
        }

        if (!HasLayerUpdatePatch(request))
        {
            throw new LayerToolException(ErrorCode.InvalidRequest, "layer_update requires at least one update field.");
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            ValidateLayerNameSyntax(request.Name);
        }

        if (!string.IsNullOrWhiteSpace(request.ObjectReference?.Name))
        {
            ValidateLayerNameSyntax(request.ObjectReference.Name);
        }

        if (request.NewName is not null)
        {
            ValidateLayerNameSyntax(request.NewName);
        }

        if (request.ColorIndex is < 1 or > 255)
        {
            throw new LayerToolException(ErrorCode.InvalidRequest, "color_index must be between 1 and 255 when provided.");
        }
    }

    private static bool HasLayerUpdatePatch(LayerUpdateRequest request) =>
        request.NewName is not null ||
        request.ColorIndex is not null ||
        request.IsOn is not null ||
        request.IsFrozen is not null ||
        request.IsLocked is not null ||
        request.IsPlottable is not null ||
        request.Description is not null;

    public static void ValidateLayerNameSyntax(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new LayerToolException(ErrorCode.InvalidLayerName, "Layer name must not be empty.");
        }

        if (name.Any(ch => ch < 32 || "<>/\\\":;?*|=,".Contains(ch)))
        {
            throw new LayerToolException(ErrorCode.InvalidLayerName, $"Layer name '{name}' contains characters that AutoCAD layer names do not allow.");
        }
    }
}

public sealed class LayerToolException : Exception
{
    public LayerToolException(ErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public LayerToolException(ErrorCode code, string message, Exception? innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public ErrorCode Code { get; }
}
