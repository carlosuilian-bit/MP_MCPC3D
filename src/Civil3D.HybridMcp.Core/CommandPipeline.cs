using System.Text.Json.Nodes;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Core;

public interface ICommandValidator<in TCommand>
{
    ValueTask ValidateAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IPermissionService
{
    ValueTask EnsureAllowedAsync(ToolDescriptor tool, ToolRequest request, CancellationToken cancellationToken);
}

public interface IConfirmationValidator
{
    ValueTask<ConfirmationValidationResult> ValidateAsync(
        ToolDescriptor tool,
        ToolRequest request,
        ConfirmationRequest confirmationRequest,
        CancellationToken cancellationToken);
}

public interface IDocumentResolver
{
    ValueTask<DocumentDescriptor> ResolveAsync(string instanceId, string? documentId, CancellationToken cancellationToken);
}

public interface IDocumentLock : IAsyncDisposable
{
}

public interface IDocumentLockProvider
{
    ValueTask<IDocumentLock> LockAsync(DocumentDescriptor document, CancellationToken cancellationToken);
}

public interface ITransactionScope : IAsyncDisposable
{
    ValueTask CommitAsync(CancellationToken cancellationToken);
    ValueTask RollbackAsync(CancellationToken cancellationToken);
}

public interface ITransactionProvider
{
    ValueTask<ITransactionScope?> BeginAsync(
        DocumentDescriptor document,
        OperationClass operationClass,
        CancellationToken cancellationToken);
}

public interface IHostApplicationDispatcher
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, T> action, CancellationToken cancellationToken);
}

public sealed class InlineHostApplicationDispatcher : IHostApplicationDispatcher
{
    public Task<T> ExecuteAsync<T>(Func<CancellationToken, T> action, CancellationToken cancellationToken) =>
        Task.FromResult(action(cancellationToken));
}

public class ToolExecutionException : Exception
{
    public ToolExecutionException(ErrorCode code, string message, JsonObject? details = null)
        : base(message)
    {
        Code = code;
        Details = details;
    }

    public ErrorCode Code { get; }
    public JsonObject? Details { get; }
}
