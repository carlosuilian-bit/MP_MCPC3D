using System.Diagnostics;
using System.Text.Json;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Tools.AutoCAD;

/// <summary>
/// Pure rules for the block reads. Every Autodesk call stays behind
/// <see cref="IBlockReadAdapter"/>, so the rules that fail silently when missed
/// can be tested without a running Civil 3D:
///
/// - a dynamic block insert points at an anonymous definition such as *U12, so
///   matching on the definition name alone returns an empty list that looks
///   like a correct answer;
/// - constant attributes live only on the definition, so reading the insert
///   alone drops them;
/// - a capped scan must say it was capped, and why.
/// </summary>
public sealed class BlockService
{
    /// <summary>Entities inspected by default. Same budget as the property set sweep, which cleared a 94,824-object drawing.</summary>
    public const int DefaultScanBudget = 250_000;

    public const int DefaultMaxResults = 2_000;

    /// <summary>Upper bound on one response, since this runs on the Autodesk idle loop.</summary>
    public const int MaxResultsCap = 10_000;

    public BlockListResponse ListBlocks(IBlockReadAdapter adapter, DocumentDescriptor descriptor, BlockListRequest request)
    {
        ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);

        var includeAnonymous = request.IncludeAnonymous ?? false;
        var includeLayouts = request.IncludeLayouts ?? false;

        var blocks = adapter.ListDefinitions()
            .Where(block => includeAnonymous || !block.IsAnonymous)
            .Where(block => includeLayouts || !block.IsLayout)
            .Where(block => string.IsNullOrWhiteSpace(request.NameContains) ||
                            block.Name.Contains(request.NameContains.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(block => block.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new BlockListResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Count = blocks.Length,
            Blocks = blocks
        };
    }

    public BlockReferenceListResponse ListReferences(
        IBlockReadAdapter adapter,
        DocumentDescriptor descriptor,
        BlockReferenceListRequest request)
    {
        ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);

        if (request.MaxScan is <= 0)
        {
            throw new BlockToolException(ErrorCode.InvalidRequest, "max_scan must be greater than zero.");
        }

        if (request.MaxResults is { } requested && (requested < 1 || requested > MaxResultsCap))
        {
            throw new BlockToolException(
                ErrorCode.InvalidRequest,
                $"max_results must be between 1 and {MaxResultsCap}; {requested} was supplied.");
        }

        var budget = request.MaxScan ?? DefaultScanBudget;
        var maxResults = request.MaxResults ?? DefaultMaxResults;
        var includeAttributes = request.IncludeAttributes ?? false;
        var includeAnonymous = request.IncludeAnonymous ?? false;
        var blockName = Normalize(request.BlockName);
        var layer = Normalize(request.Layer);

        var sweep = adapter.SweepReferences(budget, includeAttributes);

        var matches = sweep.References
            .Where(reference => blockName is null ||
                                Equal(reference.BlockName, blockName) ||
                                Equal(reference.DefinitionName, blockName))
            // A named anonymous block was asked for explicitly, so it is shown
            // even without include_anonymous.
            .Where(reference => includeAnonymous || blockName is not null || !reference.IsAnonymous)
            .Where(reference => layer is null || Equal(reference.Layer, layer))
            .Select(reference => includeAttributes ? reference : reference with { Attributes = null })
            .ToArray();

        string? truncatedBy = null;
        if (matches.Length > maxResults)
        {
            matches = matches.Take(maxResults).ToArray();
            truncatedBy = "result_limit";
        }

        // A spent scan budget outranks the result limit: it means entities were
        // never looked at, so even the matches found are only a prefix.
        if (sweep.Truncated)
        {
            truncatedBy = "scan_budget";
        }

        return new BlockReferenceListResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Count = matches.Length,
            ScannedCount = sweep.ScannedCount,
            Truncated = truncatedBy is not null,
            TruncatedBy = truncatedBy,
            References = matches
        };
    }

    public BlockGetAttributesResponse GetAttributes(
        IBlockReadAdapter adapter,
        DocumentDescriptor descriptor,
        BlockGetAttributesRequest request)
    {
        ValidateDocumentTarget(request.InstanceId, request.DocumentId, descriptor);

        var handle = Normalize(request.Handle) ?? Normalize(request.ObjectReference?.Handle)
            ?? throw new BlockToolException(
                ErrorCode.InvalidRequest,
                $"{AutoCadToolNames.BlockGetAttributes} requires handle or object_reference.handle.");

        var result = adapter.ReadAttributes(handle);

        switch (result.Status)
        {
            case BlockAttributeReadStatus.NotFound:
                throw new BlockToolException(
                    ErrorCode.ObjectNotFound,
                    $"Handle '{handle}' does not resolve to an object in the target drawing.");
            case BlockAttributeReadStatus.NotABlockReference:
                throw new BlockToolException(
                    ErrorCode.InvalidRequest,
                    $"Handle '{handle}' is a {result.ObjectClass ?? "non-block object"}, not a block reference.");
        }

        var reference = result.Reference
            ?? throw new BlockToolException(ErrorCode.InternalError, $"Handle '{handle}' was read as a block reference but returned no reference data.");

        var attributes = MergeConstantAttributes(result.InstanceAttributes, result.ConstantAttributes);

        return new BlockGetAttributesResponse
        {
            InstanceId = request.InstanceId,
            DocumentId = request.DocumentId,
            Handle = reference.Handle,
            BlockName = reference.BlockName,
            DefinitionName = reference.DefinitionName,
            IsDynamic = reference.IsDynamic,
            Count = attributes.Count,
            Attributes = attributes
        };
    }

    /// <summary>
    /// Constant attributes have no attribute reference on the insert; their value
    /// exists only on the definition. They are appended after the insert's own
    /// attributes, skipping any tag the insert already carries. Shared by
    /// <c>block_get_attributes</c> and by <c>block_reference_list</c> with
    /// attributes, so the rule lives, and is tested, in one place.
    /// </summary>
    public static IReadOnlyList<BlockAttributeInfo> MergeConstantAttributes(
        IReadOnlyList<BlockAttributeInfo> instanceAttributes,
        IReadOnlyList<BlockAttributeInfo> constantAttributes)
    {
        var carried = new HashSet<string>(instanceAttributes.Select(attribute => attribute.Tag), StringComparer.OrdinalIgnoreCase);
        return instanceAttributes
            .Concat(constantAttributes
                .Where(attribute => !carried.Contains(attribute.Tag))
                .Select(attribute => attribute with { Constant = true, Handle = null }))
            .ToArray();
    }

    private static void ValidateDocumentTarget(string instanceId, string documentId, DocumentDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new BlockToolException(ErrorCode.InvalidRequest, "instance_id is required.");
        }

        if (string.IsNullOrWhiteSpace(documentId))
        {
            throw new BlockToolException(ErrorCode.DocumentAmbiguous, "document_id is required for block tools.");
        }

        if (!string.Equals(instanceId, descriptor.InstanceId, StringComparison.Ordinal) ||
            !string.Equals(documentId, descriptor.DocumentId, StringComparison.Ordinal))
        {
            throw new BlockToolException(ErrorCode.DocumentNotFound, "Target drawing document was not found in this Civil 3D instance.");
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Equal(string? left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The narrow port the block rules need from the drawing.</summary>
public interface IBlockReadAdapter
{
    IReadOnlyList<BlockDefinitionInfo> ListDefinitions();

    /// <summary>
    /// Every block insert owned by a layout (model space or a paper space
    /// layout), stopping once <paramref name="scanBudget"/> entities have been
    /// inspected. Inserts nested inside other block definitions are part of
    /// those definitions and are not listed.
    /// </summary>
    BlockReferenceSweep SweepReferences(int scanBudget, bool includeAttributes);

    BlockAttributeReadResult ReadAttributes(string handle);
}

/// <param name="ScannedCount">Entities inspected, whether or not they were block inserts.</param>
/// <param name="Truncated">True when the scan budget ran out before every layout was read.</param>
public sealed record BlockReferenceSweep(
    IReadOnlyList<BlockReferenceInfo> References,
    int ScannedCount,
    bool Truncated);

public enum BlockAttributeReadStatus
{
    Found,
    NotFound,
    NotABlockReference
}

/// <param name="ObjectClass">Runtime class of what the handle resolved to, so a wrong-type error can name it.</param>
/// <param name="InstanceAttributes">Attributes carried by the insert itself.</param>
/// <param name="ConstantAttributes">Constant attributes from the definition, which no insert carries.</param>
public sealed record BlockAttributeReadResult(
    BlockAttributeReadStatus Status,
    string? ObjectClass,
    BlockReferenceInfo? Reference,
    IReadOnlyList<BlockAttributeInfo> InstanceAttributes,
    IReadOnlyList<BlockAttributeInfo> ConstantAttributes);

public sealed class BlockToolException : Exception
{
    public BlockToolException(ErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public ErrorCode Code { get; }
}

public sealed class AutoCadBlockToolHandler : IToolHandler
{
    private readonly ToolDescriptor _descriptor;
    private readonly string _instanceId;
    private readonly IAutoCadDocumentResolver _documents;
    private readonly IHostApplicationDispatcher _dispatcher;
    private readonly BlockService _blocks;

    public AutoCadBlockToolHandler(
        ToolDescriptor descriptor,
        string instanceId,
        IAutoCadDocumentResolver documents,
        IHostApplicationDispatcher dispatcher,
        BlockService blocks)
    {
        _descriptor = descriptor;
        _instanceId = instanceId;
        _documents = documents;
        _dispatcher = dispatcher;
        _blocks = blocks;
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
        catch (BlockToolException ex)
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
            AutoCadToolNames.BlockList => Execute<BlockListRequest>(request, input => input.InstanceId, input => input.DocumentId,
                (adapter, descriptor, input) => _blocks.ListBlocks(adapter, descriptor, input)),
            AutoCadToolNames.BlockReferenceList => Execute<BlockReferenceListRequest>(request, input => input.InstanceId, input => input.DocumentId,
                (adapter, descriptor, input) => _blocks.ListReferences(adapter, descriptor, input)),
            AutoCadToolNames.BlockGetAttributes => Execute<BlockGetAttributesRequest>(request, input => input.InstanceId, input => input.DocumentId,
                (adapter, descriptor, input) => _blocks.GetAttributes(adapter, descriptor, input)),
            _ => throw new BlockToolException(ErrorCode.ToolNotFound, $"Tool '{request.ToolName}' is not registered by the AutoCAD block handler.")
        };
    }

    private ToolExecutionResult Execute<TRequest>(
        ToolRequest request,
        Func<TRequest, string> instanceId,
        Func<TRequest, string> documentId,
        Func<IBlockReadAdapter, DocumentDescriptor, TRequest, object> run)
    {
        var input = ReadArguments<TRequest>(request);
        EnsureInstance(instanceId(input));
        var target = _documents.Resolve(documentId(input));
        var adapter = new AutodeskBlockAdapter(target.Document);
        return new ToolExecutionResult(target.Descriptor, run(adapter, target.Descriptor, input));
    }

    private void EnsureInstance(string requestedInstanceId)
    {
        if (!string.Equals(requestedInstanceId, _instanceId, StringComparison.Ordinal))
        {
            throw new BlockToolException(ErrorCode.InstanceNotFound, "Block request instance_id does not match this Civil 3D bridge.");
        }
    }

    private static T ReadArguments<T>(ToolRequest request)
    {
        if (request.Arguments is null)
        {
            throw new BlockToolException(ErrorCode.InvalidRequest, "Block tool request requires structured arguments.");
        }

        return request.Arguments.Deserialize<T>(JsonContractOptions.Default)
            ?? throw new BlockToolException(ErrorCode.InvalidRequest, "Block tool request arguments could not be read.");
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
