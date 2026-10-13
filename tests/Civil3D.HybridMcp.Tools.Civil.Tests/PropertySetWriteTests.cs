using System.Text.Json;
using Civil3D.HybridMcp.Core;
using Civil3D.HybridMcp.Protocol;
using Civil3D.HybridMcp.Tools.Civil;

namespace Civil3D.HybridMcp.Tools.Civil.Tests;

/// <summary>
/// Phase 3E: writing an AEC property set on a production object.
///
/// This is the first tool in the platform that requires confirmation. Every
/// earlier write was bounded to something the platform had just created itself,
/// a new layer or a temporary DBPoint. This one changes data an engineer put
/// there, so the rule is that nothing is written until a person has seen exactly
/// what would change and said yes to that exact change.
/// </summary>
public sealed class PropertySetWriteTests
{
    private const string Handle = "16FD0D";
    private const string SetName = "Dados_Rodovia";

    [Fact]
    public void ResponseSerializesWithStructuredIdentity()
    {
        var response = new PropertySetUpdateResponse
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Handle = Handle,
            SetName = SetName,
            Applied = true,
            ChangedCount = 1,
            Properties =
            [
                new PropertySetValueChange
                {
                    Name = "Km",
                    DataType = "Real",
                    PreviousValue = "529",
                    PreviousNumericValue = 529,
                    NewValue = "530",
                    NewNumericValue = 530,
                    Changed = true
                }
            ]
        };

        var json = JsonSerializer.Serialize(response, JsonContractOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<PropertySetUpdateResponse>(json, JsonContractOptions.Default);

        Assert.Contains("\"set_name\"", json);
        Assert.Contains("\"changed_count\"", json);
        Assert.Contains("\"previous_value\"", json);
        Assert.Contains("\"previous_numeric_value\"", json);
        Assert.Contains("\"new_numeric_value\"", json);
        Assert.NotNull(roundTrip);
        Assert.Equal(530, roundTrip!.Properties[0].NewNumericValue);
    }

    [Fact]
    public void RequiresHandleSetNameAndAtLeastOneProperty()
    {
        var service = new PropertySetWriteService(new FakeConfirmationGate());

        Assert.Equal(ErrorCode.InvalidRequest, Throws(service, Request() with { Handle = "  " }).Code);
        Assert.Equal(ErrorCode.InvalidRequest, Throws(service, Request() with { SetName = "  " }).Code);
        Assert.Equal(ErrorCode.InvalidRequest, Throws(service, Request() with { Properties = [] }).Code);
    }

    [Fact]
    public void ReportsObjectNotFoundWhenTheHandleDoesNotResolve()
    {
        var service = new PropertySetWriteService(new FakeConfirmationGate());
        var adapter = new FakePropertySetWriteAdapter(objectFound: false, propertySet: null);

        var error = Assert.Throws<CivilToolException>(
            () => service.SetPropertySet(adapter, Document(), Request(), Context()));

        Assert.Equal(ErrorCode.ObjectNotFound, error.Code);
        Assert.Empty(adapter.Applied);
    }

    [Fact]
    public void ReportsPropertySetNotFoundWhenTheSetIsNotAttached()
    {
        var service = new PropertySetWriteService(new FakeConfirmationGate());
        var adapter = new FakePropertySetWriteAdapter(objectFound: true, propertySet: null);

        var error = Assert.Throws<CivilToolException>(
            () => service.SetPropertySet(adapter, Document(), Request(), Context()));

        // Attaching a set the engineer did not put there is a different, larger
        // decision. This tool only updates what is already attached.
        Assert.Equal(ErrorCode.PropertySetNotFound, error.Code);
        Assert.Empty(adapter.Applied);
    }

    [Fact]
    public void ReportsPropertyNotFoundWhenTheSetDoesNotDefineIt()
    {
        var service = new PropertySetWriteService(new FakeConfirmationGate());
        var adapter = new FakePropertySetWriteAdapter(objectFound: true, propertySet: SampleSet());
        var request = Request() with
        {
            Properties = [new PropertySetValueUpdate { Name = "NaoExiste", Value = "1" }]
        };

        var error = Assert.Throws<CivilToolException>(
            () => service.SetPropertySet(adapter, Document(), request, Context()));

        Assert.Equal(ErrorCode.PropertyNotFound, error.Code);
        Assert.Empty(adapter.Applied);
    }

    [Fact]
    public void WithoutAConfirmationTokenNothingIsWrittenAndThePreviewIsReturned()
    {
        var gate = new FakeConfirmationGate();
        var service = new PropertySetWriteService(gate);
        var adapter = new FakePropertySetWriteAdapter(objectFound: true, propertySet: SampleSet());

        var error = Assert.Throws<CivilToolException>(
            () => service.SetPropertySet(adapter, Document(), Request(), Context()));

        Assert.Equal(ErrorCode.ConfirmationRequired, error.Code);
        Assert.Empty(adapter.Applied);

        // The preview has to name the object, the set, and the before and after
        // of every value, or the person is being asked to approve a blank cheque.
        Assert.NotNull(gate.LastRequest);
        Assert.Equal(1, gate.LastRequest!.AffectedCount);
        Assert.Contains(SetName, gate.LastRequest.PreviewSummary);
        Assert.Contains("529", gate.LastRequest.PreviewSummary);
        Assert.Contains("530", gate.LastRequest.PreviewSummary);
    }

    [Fact]
    public void TheRefusalCarriesBackTheTokenTheCallerMustConfirmWith()
    {
        var gate = new FakeConfirmationGate { IssuedToken = SampleToken() };
        var service = new PropertySetWriteService(gate);
        var adapter = new FakePropertySetWriteAdapter(objectFound: true, propertySet: SampleSet());

        var error = Assert.Throws<CivilToolException>(
            () => service.SetPropertySet(adapter, Document(), Request(), Context()));

        // Telling a caller to confirm and giving them nothing to confirm with
        // would make the whole flow unusable end to end.
        Assert.Equal(ErrorCode.ConfirmationRequired, error.Code);
        Assert.NotNull(error.Details);
        Assert.Equal(1, error.Details!["affected_count"]!.GetValue<int>());
        Assert.Contains(SetName, error.Details["preview"]!.GetValue<string>());
        Assert.Equal("token-1", error.Details["confirmation_token"]!["tokenId"]!.GetValue<string>());
    }

    [Fact]
    public void AnInvalidTokenIsRejectedBeforeAnythingIsWritten()
    {
        var gate = new FakeConfirmationGate { Outcome = ConfirmationDecision.Invalid };
        var service = new PropertySetWriteService(gate);
        var adapter = new FakePropertySetWriteAdapter(objectFound: true, propertySet: SampleSet());

        var error = Assert.Throws<CivilToolException>(
            () => service.SetPropertySet(adapter, Document(), Request(), Context(SampleToken())));

        Assert.Equal(ErrorCode.ConfirmationInvalid, error.Code);
        Assert.Empty(adapter.Applied);
    }

    [Fact]
    public void AnExpiredTokenIsRejectedBeforeAnythingIsWritten()
    {
        var gate = new FakeConfirmationGate { Outcome = ConfirmationDecision.Expired };
        var service = new PropertySetWriteService(gate);
        var adapter = new FakePropertySetWriteAdapter(objectFound: true, propertySet: SampleSet());

        var error = Assert.Throws<CivilToolException>(
            () => service.SetPropertySet(adapter, Document(), Request(), Context(SampleToken())));

        Assert.Equal(ErrorCode.ConfirmationExpired, error.Code);
        Assert.Empty(adapter.Applied);
    }

    [Fact]
    public void AnApprovedWriteAppliesAndReportsBeforeAndAfter()
    {
        var gate = new FakeConfirmationGate { Outcome = ConfirmationDecision.Approved };
        var service = new PropertySetWriteService(gate);
        var adapter = new FakePropertySetWriteAdapter(objectFound: true, propertySet: SampleSet());

        var response = service.SetPropertySet(adapter, Document(), Request(), Context(SampleToken()));

        Assert.True(response.Applied);
        Assert.Equal(1, response.ChangedCount);
        var change = Assert.Single(response.Properties);
        Assert.Equal("Km", change.Name);
        Assert.Equal("529", change.PreviousValue);
        Assert.Equal(529, change.PreviousNumericValue);
        Assert.Equal("530", change.NewValue);
        Assert.True(change.Changed);
        Assert.Single(adapter.Applied);
    }

    [Fact]
    public void ARequestThatChangesNothingIsRejectedBeforeAskingForConfirmation()
    {
        var gate = new FakeConfirmationGate();
        var service = new PropertySetWriteService(gate);
        var adapter = new FakePropertySetWriteAdapter(objectFound: true, propertySet: SampleSet());
        var request = Request() with
        {
            Properties = [new PropertySetValueUpdate { Name = "Km", Value = "529" }]
        };

        var error = Assert.Throws<CivilToolException>(
            () => service.SetPropertySet(adapter, Document(), request, Context()));

        // Asking a person to approve a write that changes nothing trains them to
        // approve without reading.
        Assert.Equal(ErrorCode.InvalidRequest, error.Code);
        Assert.Null(gate.LastRequest);
        Assert.Empty(adapter.Applied);
    }

    [Fact]
    public void AffectedCountCoversOnlyThePropertiesThatWouldChange()
    {
        var gate = new FakeConfirmationGate();
        var service = new PropertySetWriteService(gate);
        var adapter = new FakePropertySetWriteAdapter(objectFound: true, propertySet: SampleSet());
        var request = Request() with
        {
            Properties =
            [
                new PropertySetValueUpdate { Name = "Km", Value = "530" },
                new PropertySetValueUpdate { Name = "Rodovia", Value = "BR-153" }
            ]
        };

        Assert.Throws<CivilToolException>(() => service.SetPropertySet(adapter, Document(), request, Context()));

        // "Rodovia" already holds BR-153, so only one value actually changes.
        Assert.Equal(1, gate.LastRequest!.AffectedCount);
    }

    [Fact]
    public void CatalogPublishesPropertySetSetAsAConfirmedWrite()
    {
        var tool = Assert.Single(CivilToolCatalog.ListTools(), descriptor => descriptor.Name == "propertyset_set");

        Assert.Equal(OperationClass.Write, tool.OperationClass);
        Assert.Equal(ToolRisk.Medium, tool.Risk);
        Assert.True(tool.RequiresConfirmation);
        Assert.Equal(RollbackSemantics.TransactionRollbackBeforeCommit, tool.RollbackSemantics);
        Assert.Equal("Civil3D", tool.Module);
    }

    private static CivilToolException Throws(PropertySetWriteService service, PropertySetUpdateRequest request) =>
        Assert.Throws<CivilToolException>(() => service.SetPropertySet(
            new FakePropertySetWriteAdapter(objectFound: true, propertySet: SampleSet()),
            Document(),
            request,
            Context()));

    private static PropertySetUpdateRequest Request() =>
        new()
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Handle = Handle,
            SetName = SetName,
            Properties = [new PropertySetValueUpdate { Name = "Km", Value = "530" }]
        };

    private static PropertySetWriteContext Context(ConfirmationToken? token = null) =>
        new("correlation-1", token);

    private static ConfirmationToken SampleToken() =>
        new()
        {
            TokenId = "token-1",
            ToolName = "propertyset_set",
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            CorrelationId = "correlation-1",
            ArgumentsHash = "hash",
            AffectedCount = 1,
            OperationClass = OperationClass.Write,
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5),
            Signature = "signature"
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

    private static PropertySetInfo SampleSet() =>
        new()
        {
            Name = SetName,
            Handle = "16FD10",
            DefinitionHandle = "16FD0C",
            PropertyCount = 2,
            Properties =
            [
                new PropertyValueInfo
                {
                    Name = "Km",
                    DataType = "Real",
                    Value = "529",
                    NumericValue = 529,
                    IsReadOnly = false
                },
                new PropertyValueInfo
                {
                    Name = "Rodovia",
                    DataType = "Text",
                    Value = "BR-153",
                    IsReadOnly = false
                }
            ]
        };

    private sealed class FakePropertySetWriteAdapter : IPropertySetWriteAdapter
    {
        private readonly bool _objectFound;
        private readonly PropertySetInfo? _propertySet;

        public FakePropertySetWriteAdapter(bool objectFound, PropertySetInfo? propertySet)
        {
            _objectFound = objectFound;
            _propertySet = propertySet;
        }

        public List<IReadOnlyList<PropertySetValueUpdate>> Applied { get; } = [];

        public PropertySetReadState Read(string handle, string setName) =>
            new(_objectFound, _propertySet);

        public IReadOnlyList<PropertyValueInfo> Apply(
            string handle,
            string setName,
            IReadOnlyList<PropertySetValueUpdate> updates)
        {
            Applied.Add(updates);
            return updates
                .Select(update => new PropertyValueInfo
                {
                    Name = update.Name,
                    DataType = "Real",
                    Value = update.Value,
                    NumericValue = double.TryParse(update.Value, out var number) ? number : null
                })
                .ToArray();
        }
    }

    private sealed class FakeConfirmationGate : IConfirmationGate
    {
        public ConfirmationDecision Outcome { get; init; } = ConfirmationDecision.Required;

        public ConfirmationRequest? LastRequest { get; private set; }

        public ConfirmationEvaluation Evaluate(ConfirmationRequest request, ConfirmationToken? token)
        {
            LastRequest = request;
            return new ConfirmationEvaluation(Outcome, Outcome == ConfirmationDecision.Required ? IssuedToken : null);
        }

        public ConfirmationToken? IssuedToken { get; init; }
    }
}
