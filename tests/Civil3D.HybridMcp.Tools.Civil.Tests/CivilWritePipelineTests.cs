using Civil3D.HybridMcp.Protocol;
using Civil3D.HybridMcp.Tools.Civil;

namespace Civil3D.HybridMcp.Tools.Civil.Tests;

/// <summary>
/// Phase 3D: rollback regression for the Civil write pipeline.
///
/// This is the pipeline the Phase 3C AEC property-set write runs through, and it
/// had no coverage at all. The cases below are the ones that decide whether a
/// failed write leaves the drawing clean, and whether the operator can still
/// find out what went wrong afterwards.
/// </summary>
public sealed class CivilWritePipelineTests
{
    [Fact]
    public void CommitsOnSuccess()
    {
        var scope = new FakeCivilWriteScope("context");
        var pipeline = new CivilWritePipeline<string>(new FakeCivilWriteScopeFactory(scope));

        var result = pipeline.Execute(context => context.ToUpperInvariant());

        Assert.Equal("CONTEXT", result.Data);
        Assert.True(result.Transaction.DocumentLocked);
        Assert.True(result.Transaction.TransactionStarted);
        Assert.True(result.Transaction.Committed);
        Assert.False(result.Transaction.RolledBack);
    }

    [Fact]
    public void RollsBackOnErrorAndLetsTheOriginalFailureThrough()
    {
        var scope = new FakeCivilWriteScope("context");
        var pipeline = new CivilWritePipeline<string>(new FakeCivilWriteScopeFactory(scope));

        var error = Assert.Throws<CivilToolException>(
            () => pipeline.Execute<string>(_ => throw new CivilToolException(ErrorCode.PropertySetNotFound, "set is not attached")));

        Assert.Equal(ErrorCode.PropertySetNotFound, error.Code);
        Assert.Equal("set is not attached", error.Message);
        Assert.False(scope.State.Committed);
        Assert.True(scope.State.RolledBack);
    }

    [Fact]
    public void DisposeRollsBackWhenTheOperationNeverCompleted()
    {
        var scope = new FakeCivilWriteScope("context");

        using (scope)
        {
            // Abandoned without commit or rollback, which is what happens if a
            // caller bypasses the pipeline and then drops the scope.
        }

        Assert.True(scope.Disposed);
        Assert.True(scope.State.RolledBack);
        Assert.False(scope.State.Committed);
    }

    [Fact]
    public void RollbackAfterCommitDoesNotUndoTheCommittedState()
    {
        var scope = new FakeCivilWriteScope("context");
        var pipeline = new CivilWritePipeline<string>(new FakeCivilWriteScopeFactory(scope));

        pipeline.Execute(context => context);
        scope.Rollback();

        Assert.True(scope.State.Committed);
        Assert.False(scope.State.RolledBack);
        Assert.Equal(1, scope.CommitCalls);
    }

    [Fact]
    public void RollbackFailureIsReportedWithoutLosingTheOriginalCause()
    {
        var scope = new FakeCivilWriteScope("context") { FailRollback = true };
        var pipeline = new CivilWritePipeline<string>(new FakeCivilWriteScopeFactory(scope));

        var error = Assert.Throws<CivilToolException>(
            () => pipeline.Execute<string>(_ => throw new CivilToolException(ErrorCode.ObjectNotFound, "handle did not resolve")));

        // Rollback failing is the more dangerous outcome, so that is the code the
        // caller sees. The reason the write failed must still be reachable, or
        // the operator is told the drawing may be dirty and never learns why.
        Assert.Equal(ErrorCode.RollbackFailed, error.Code);
        var cause = Assert.IsType<CivilToolException>(error.InnerException);
        Assert.Equal(ErrorCode.ObjectNotFound, cause.Code);
        Assert.Equal("handle did not resolve", cause.Message);
    }

    [Fact]
    public void DisposeDoesNotThrowWhenRollbackFails()
    {
        var scope = new FakeCivilWriteScope("context") { FailRollback = true };

        // A throwing Dispose replaces whatever exception is already propagating,
        // which is how the real cause of a failed write got lost.
        var failure = Record.Exception(() => scope.Dispose());

        Assert.Null(failure);
        Assert.True(scope.Disposed);
    }

    private sealed class FakeCivilWriteScopeFactory : ICivilWriteScopeFactory<string>
    {
        private readonly FakeCivilWriteScope _scope;

        public FakeCivilWriteScopeFactory(FakeCivilWriteScope scope)
        {
            _scope = scope;
        }

        public ICivilWriteScope<string> Begin() => _scope;
    }

    private sealed class FakeCivilWriteScope : ICivilWriteScope<string>
    {
        private bool _completed;

        public FakeCivilWriteScope(string context)
        {
            Context = context;
            State = new WriteTransactionInfo
            {
                DocumentLocked = true,
                TransactionStarted = true
            };
        }

        public string Context { get; }

        public WriteTransactionInfo State { get; private set; }

        public bool Disposed { get; private set; }

        public bool FailRollback { get; init; }

        public int CommitCalls { get; private set; }

        public void Commit()
        {
            if (_completed)
            {
                return;
            }

            CommitCalls++;
            _completed = true;
            State = State with { Committed = true, RolledBack = false };
        }

        public void Rollback()
        {
            if (_completed)
            {
                return;
            }

            if (FailRollback)
            {
                throw new CivilToolException(ErrorCode.RollbackFailed, "Could not roll back the AutoCAD transaction: abort failed.");
            }

            _completed = true;
            State = State with { RolledBack = true, Committed = false };
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
                // Mirrors the real scope: Dispose never throws.
            }

            Disposed = true;
        }
    }
}
