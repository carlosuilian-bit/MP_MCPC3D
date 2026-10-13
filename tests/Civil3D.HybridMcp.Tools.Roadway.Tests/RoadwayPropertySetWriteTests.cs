using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;
using Civil3D.HybridMcp.Tools.Roadway;

namespace Civil3D.HybridMcp.Tools.Roadway.Tests;

/// <summary>
/// Phase 6B: updating a property set across many objects at once.
///
/// This composes the three things live validation already proved: finding the
/// objects, reading their current values, and writing with confirmation. What it
/// adds is the batch, and a batch write is a different animal from a batch read.
///
/// Two rules shape it. First, the write is **atomic**: reading half a list is
/// useful, but 526 half-updated kilometre markers is a worse state than none, so
/// any failure undoes everything. Second, it takes **explicit handles**, never a
/// filter, so the platform can never expand a query into a write the operator
/// did not see.
/// </summary>
public sealed class RoadwayPropertySetWriteTests
{
    private const string SetName = "Dados_Rodovia";

    [Fact]
    public void WritesEveryObjectOnceConfirmed()
    {
        var adapter = new FakeAdapter();
        var response = Write(adapter, Request(
            Target("A1", "530"),
            Target("B2", "531")), Approved());

        Assert.True(response.Applied);
        Assert.Equal(2, response.ObjectCount);
        Assert.Equal(2, response.ChangedCount);
        Assert.Equal(2, adapter.Applied.Count);
        Assert.Collection(
            response.Objects,
            item => { Assert.Equal("A1", item.Handle); Assert.True(item.Changed); },
            item => { Assert.Equal("B2", item.Handle); Assert.True(item.Changed); });
    }

    [Fact]
    public void WithoutAConfirmationNothingIsWrittenAndThePreviewSummarises()
    {
        var gate = new FakeGate();
        var adapter = new FakeAdapter();

        var error = Assert.Throws<RoadwayToolException>(
            () => Write(adapter, Request(Target("A1", "530"), Target("B2", "531")), gate));

        Assert.Equal(ErrorCode.ConfirmationRequired, error.Code);
        Assert.Empty(adapter.Applied);

        // A preview of 526 lines is a preview nobody reads. It has to say how
        // much is changing, in which set and drawing, and show enough of the
        // change to be recognisable.
        Assert.Equal(2, gate.LastRequest!.AffectedCount);
        Assert.Contains(SetName, gate.LastRequest.PreviewSummary);
        Assert.Contains("2 object", gate.LastRequest.PreviewSummary);
    }

    [Fact]
    public void TheRefusalCarriesBackTheTokenTheCallerMustConfirmWith()
    {
        var gate = new FakeGate { IssuedToken = SampleToken() };

        var error = Assert.Throws<RoadwayToolException>(
            () => Write(new FakeAdapter(), Request(Target("A1", "530")), gate));

        // Live validation of this phase found the token missing here, the same
        // defect Phase 3E had. A caller told to confirm needs the thing to
        // confirm with.
        Assert.Equal(ErrorCode.ConfirmationRequired, error.Code);
        Assert.NotNull(error.Details);
        Assert.Equal("token-1", error.Details!["confirmation_token"]!["tokenId"]!.GetValue<string>());
    }

    private static ConfirmationToken SampleToken() =>
        new()
        {
            TokenId = "token-1",
            ToolName = "roadway_propertyset_write",
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            CorrelationId = "corr-1",
            ArgumentsHash = "hash",
            AffectedCount = 1,
            OperationClass = OperationClass.Write,
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5),
            Signature = "sig"
        };

    [Fact]
    public void OneFailingObjectUndoesTheWholeBatch()
    {
        var adapter = new FakeAdapter { MissingHandles = ["B2"] };

        var error = Assert.Throws<RoadwayToolException>(
            () => Write(adapter, Request(Target("A1", "530"), Target("B2", "531")), Approved()));

        // Half-updated markers are worse than none, so nothing is written at all.
        Assert.Equal(ErrorCode.ObjectNotFound, error.Code);
        Assert.Empty(adapter.Applied);
    }

    [Fact]
    public void AnObjectNotCarryingTheSetFailsTheBatch()
    {
        var adapter = new FakeAdapter { WithoutSet = ["B2"] };

        var error = Assert.Throws<RoadwayToolException>(
            () => Write(adapter, Request(Target("A1", "530"), Target("B2", "531")), Approved()));

        Assert.Equal(ErrorCode.PropertySetNotFound, error.Code);
        Assert.Empty(adapter.Applied);
    }

    [Fact]
    public void AnUndefinedPropertyFailsTheBatch()
    {
        var adapter = new FakeAdapter();
        var request = Request(Target("A1", "530")) with
        {
            Objects = [new RoadwayPropertySetTarget
            {
                Handle = "A1",
                Properties = [new PropertySetValueUpdate { Name = "NaoExiste", Value = "1" }]
            }]
        };

        var error = Assert.Throws<RoadwayToolException>(() => Write(adapter, request, Approved()));

        Assert.Equal(ErrorCode.PropertyNotFound, error.Code);
        Assert.Empty(adapter.Applied);
    }

    [Fact]
    public void ObjectsThatWouldNotChangeAreSkippedNotWritten()
    {
        var adapter = new FakeAdapter();

        // A1 already holds 529.
        var response = Write(adapter, Request(Target("A1", "529"), Target("B2", "531")), Approved());

        Assert.Equal(2, response.ObjectCount);
        Assert.Equal(1, response.ChangedCount);
        Assert.Equal(1, response.SkippedCount);
        Assert.False(response.Objects[0].Changed);
        Assert.Single(adapter.Applied);
    }

    [Fact]
    public void ABatchWhereNothingWouldChangeIsRejectedBeforeAskingForConfirmation()
    {
        var gate = new FakeGate();

        var error = Assert.Throws<RoadwayToolException>(
            () => Write(new FakeAdapter(), Request(Target("A1", "529")), gate));

        Assert.Equal(ErrorCode.InvalidRequest, error.Code);
        Assert.Null(gate.LastRequest);
    }

    [Fact]
    public void RejectsADuplicatedHandle()
    {
        var error = Assert.Throws<RoadwayToolException>(
            () => Write(new FakeAdapter(), Request(Target("A1", "530"), Target("A1", "531")), Approved()));

        // Two updates to one object in one batch make the outcome depend on order.
        Assert.Equal(ErrorCode.InvalidRequest, error.Code);
    }

    [Fact]
    public void RequiresSetNameAndAtLeastOneObject()
    {
        Assert.Equal(
            ErrorCode.InvalidRequest,
            Assert.Throws<RoadwayToolException>(
                () => Write(new FakeAdapter(), Request(Target("A1", "530")) with { SetName = " " }, Approved())).Code);

        Assert.Equal(
            ErrorCode.InvalidRequest,
            Assert.Throws<RoadwayToolException>(
                () => Write(new FakeAdapter(), Request() with { Objects = [] }, Approved())).Code);
    }

    [Fact]
    public void RejectsABatchLargerThanTheCap()
    {
        var targets = Enumerable.Range(0, RoadwayPropertySetWriteService.MaxObjects + 1)
            .Select(index => Target("H" + index, "530"))
            .ToArray();

        var error = Assert.Throws<RoadwayToolException>(
            () => Write(new FakeAdapter(), Request(targets), Approved()));

        Assert.Equal(ErrorCode.InvalidRequest, error.Code);
    }

    [Fact]
    public void CatalogPublishesTheBatchWriteAsConfirmed()
    {
        var tool = Assert.Single(RoadwayToolCatalog.ListTools(), t => t.Name == "roadway_propertyset_write");

        Assert.Equal(OperationClass.Write, tool.OperationClass);
        Assert.Equal(ToolRisk.Medium, tool.Risk);
        Assert.True(tool.RequiresConfirmation);
        Assert.Equal(RollbackSemantics.TransactionRollbackBeforeCommit, tool.RollbackSemantics);
        Assert.Equal("Roadway", tool.Module);
    }

    private static RoadwayPropertySetWriteResponse Write(
        FakeAdapter adapter,
        RoadwayPropertySetWriteRequest request,
        IConfirmationGate gate) =>
        new RoadwayPropertySetWriteService(gate).Write(adapter, Document(), request, new RoadwayWriteContext("corr-1", null));

    private static IConfirmationGate Approved() => new FakeGate { Outcome = ConfirmationDecision.Approved };

    private static RoadwayPropertySetTarget Target(string handle, string secao) =>
        new()
        {
            Handle = handle,
            Properties = [new PropertySetValueUpdate { Name = "Secao", Value = secao }]
        };

    private static RoadwayPropertySetWriteRequest Request(params RoadwayPropertySetTarget[] targets) =>
        new()
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            SetName = SetName,
            Objects = targets
        };

    private static DocumentDescriptor Document() =>
        new()
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "Way-153.dwg",
            FullPath = null,
            IsActive = true,
            IsSaved = true,
            IsReadOnly = false,
            Units = "Meters",
            DatabaseFingerprint = "fingerprint-1",
            SessionIdentifier = "session-1"
        };

    private sealed class FakeAdapter : IRoadwayPropertySetAdapter
    {
        public IReadOnlyList<string> MissingHandles { get; init; } = [];
        public IReadOnlyList<string> WithoutSet { get; init; } = [];
        public List<string> Applied { get; } = [];

        public RoadwayPropertySetState Read(string handle, string setName)
        {
            if (MissingHandles.Contains(handle))
            {
                return new RoadwayPropertySetState(false, null);
            }

            if (WithoutSet.Contains(handle))
            {
                return new RoadwayPropertySetState(true, null);
            }

            return new RoadwayPropertySetState(true, new PropertySetInfo
            {
                Name = setName,
                Handle = handle + "S",
                PropertyCount = 1,
                Properties = [new PropertyValueInfo { Name = "Secao", DataType = "Text", Value = "529" }]
            });
        }

        public IReadOnlyList<PropertyValueInfo> Apply(
            string handle,
            string setName,
            IReadOnlyList<PropertySetValueUpdate> updates)
        {
            Applied.Add(handle);
            return updates
                .Select(update => new PropertyValueInfo { Name = update.Name, DataType = "Text", Value = update.Value })
                .ToArray();
        }
    }

    private sealed class FakeGate : IConfirmationGate
    {
        public ConfirmationDecision Outcome { get; init; } = ConfirmationDecision.Required;
        public ConfirmationRequest? LastRequest { get; private set; }

        public ConfirmationToken? IssuedToken { get; init; }

        public ConfirmationEvaluation Evaluate(ConfirmationRequest request, ConfirmationToken? token)
        {
            LastRequest = request;
            return new ConfirmationEvaluation(Outcome, IssuedToken);
        }
    }
}
