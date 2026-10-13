using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Tools.Civil;

public interface ICivilWriteScope<out TContext> : IDisposable
{
    TContext Context { get; }
    WriteTransactionInfo State { get; }
    void Commit();
    void Rollback();
}

public interface ICivilWriteScopeFactory<out TContext>
{
    ICivilWriteScope<TContext> Begin();
}

public sealed record CivilWritePipelineResult<T>(T Data, WriteTransactionInfo Transaction);

public sealed class CivilWritePipeline<TContext>
{
    private readonly ICivilWriteScopeFactory<TContext> _factory;

    public CivilWritePipeline(ICivilWriteScopeFactory<TContext> factory)
    {
        _factory = factory;
    }

    public CivilWritePipelineResult<T> Execute<T>(Func<TContext, T> operation)
    {
        using var scope = _factory.Begin();
        try
        {
            var data = operation(scope.Context);
            scope.Commit();
            return new CivilWritePipelineResult<T>(data, scope.State);
        }
        catch (Exception operationFailure)
        {
            try
            {
                scope.Rollback();
            }
            catch (CivilToolException rollbackFailure)
            {
                // A failed rollback outranks a failed write: the drawing may be
                // left mid-change, so that is the code the caller must see. The
                // original cause rides along as the inner exception, otherwise
                // the operator is warned the drawing may be dirty and never
                // learns what was being attempted.
                throw new CivilToolException(rollbackFailure.Code, rollbackFailure.Message, operationFailure);
            }

            throw;
        }
    }
}

public sealed record AutodeskCivilWriteContext(Database Database, Transaction Transaction);

public sealed class AutodeskCivilWriteScopeFactory : ICivilWriteScopeFactory<AutodeskCivilWriteContext>
{
    private readonly Document _document;

    public AutodeskCivilWriteScopeFactory(Document document)
    {
        _document = document;
    }

    public ICivilWriteScope<AutodeskCivilWriteContext> Begin() => new AutodeskCivilWriteScope(_document);
}

public sealed class AutodeskCivilWriteScope : ICivilWriteScope<AutodeskCivilWriteContext>
{
    private readonly DocumentLock _lock;
    private readonly Transaction _transaction;
    private bool _completed;

    public AutodeskCivilWriteScope(Document document)
    {
        try
        {
            _lock = document.LockDocument();
            State = State with { DocumentLocked = true };
        }
        catch (Exception ex)
        {
            throw new CivilToolException(ErrorCode.DocumentLockFailed, $"Could not lock the target drawing: {ex.Message}");
        }

        try
        {
            _transaction = document.Database.TransactionManager.StartTransaction();
            State = State with { TransactionStarted = true };
            Context = new AutodeskCivilWriteContext(document.Database, _transaction);
        }
        catch (Exception ex)
        {
            _lock.Dispose();
            throw new CivilToolException(ErrorCode.TransactionFailed, $"Could not start an AutoCAD transaction: {ex.Message}");
        }
    }

    public AutodeskCivilWriteContext Context { get; }
    public WriteTransactionInfo State { get; private set; } = new();

    public void Commit()
    {
        if (_completed)
        {
            return;
        }

        try
        {
            _transaction.Commit();
            _completed = true;
            State = State with { Committed = true, RolledBack = false };
        }
        catch (Exception ex)
        {
            throw new CivilToolException(ErrorCode.TransactionFailed, $"Could not commit the AutoCAD transaction: {ex.Message}");
        }
    }

    public void Rollback()
    {
        if (_completed)
        {
            return;
        }

        try
        {
            _transaction.Abort();
            _completed = true;
            State = State with { RolledBack = true, Committed = false };
        }
        catch (Exception ex)
        {
            throw new CivilToolException(ErrorCode.RollbackFailed, $"Could not roll back the AutoCAD transaction: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_completed)
            {
                Rollback();
            }
        }
        catch (CivilToolException)
        {
            // Dispose must not throw. It runs while another exception may already
            // be propagating, and throwing here would replace the real cause with
            // this one. A rollback failure that matters is reported by the
            // explicit Rollback() call in the write pipeline.
        }
        finally
        {
            _transaction.Dispose();
            _lock.Dispose();
        }
    }
}
