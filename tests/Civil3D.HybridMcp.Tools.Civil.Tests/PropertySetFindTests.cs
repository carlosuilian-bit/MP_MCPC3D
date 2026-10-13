using Civil3D.HybridMcp.Protocol;
using Civil3D.HybridMcp.Tools.Civil;

namespace Civil3D.HybridMcp.Tools.Civil.Tests;

/// <summary>
/// Phase 2H: finding the objects that carry a property set.
///
/// Live validation of `propertyset_set` exposed the gap this closes. Every
/// property-set tool takes a handle, so using them meant already knowing the
/// handle, and the only way to learn one was to sweep every object by hand. The
/// handle used in that live test came out of an old report, not out of the
/// platform.
///
/// This is the read that makes the write usable at scale: name a set, optionally
/// name values, get back the objects that match.
/// </summary>
public sealed class PropertySetFindTests
{
    private const string SetName = "Dados_Rodovia";

    [Fact]
    public void FindsEveryObjectCarryingTheSet()
    {
        var response = Find(Request());

        Assert.Equal(3, response.Count);
        Assert.Equal(SetName, response.SetName);
        Assert.Collection(
            response.Objects,
            item => Assert.Equal("A1", item.Handle),
            item => Assert.Equal("B2", item.Handle),
            item => Assert.Equal("C3", item.Handle));
        Assert.All(response.Objects, item => Assert.Equal(SetName, item.PropertySet.Name));
    }

    [Fact]
    public void ReportsHowManyObjectsWereScanned()
    {
        var response = Find(Request());

        // A caller that gets three results out of four thousand objects should be
        // able to see that the sweep really covered the drawing.
        Assert.Equal(4146, response.ScannedCount);
        Assert.False(response.Truncated);
    }

    [Fact]
    public void FiltersByAnExactPropertyValue()
    {
        var response = Find(Request() with
        {
            Match = [new PropertySetValueFilter { Name = "Secao", EqualsValue = "530" }]
        });

        var found = Assert.Single(response.Objects);
        Assert.Equal("B2", found.Handle);
    }

    [Fact]
    public void FiltersByASubstringWhenAskedTo()
    {
        var response = Find(Request() with
        {
            Match = [new PropertySetValueFilter { Name = "Ponto_A", Contains = "km-53" }]
        });

        Assert.Equal(2, response.Count);
        Assert.Collection(
            response.Objects,
            item => Assert.Equal("B2", item.Handle),
            item => Assert.Equal("C3", item.Handle));
    }

    [Fact]
    public void CombinesFiltersWithAnd()
    {
        var response = Find(Request() with
        {
            Match =
            [
                new PropertySetValueFilter { Name = "BR", EqualsValue = "153" },
                new PropertySetValueFilter { Name = "Secao", EqualsValue = "531" }
            ]
        });

        var found = Assert.Single(response.Objects);
        Assert.Equal("C3", found.Handle);
    }

    [Fact]
    public void AnObjectMissingTheFilteredPropertyDoesNotMatch()
    {
        var response = Find(Request() with
        {
            Match = [new PropertySetValueFilter { Name = "NaoExiste", EqualsValue = "qualquer" }]
        });

        Assert.Empty(response.Objects);
        Assert.Equal(0, response.Count);
    }

    [Fact]
    public void NoMatchIsAnEmptyResultNotAnError()
    {
        var response = Find(Request() with
        {
            Match = [new PropertySetValueFilter { Name = "Secao", EqualsValue = "999" }]
        });

        // "No object carries that value" is an answer, not a failure.
        Assert.Equal(0, response.Count);
        Assert.Equal(4146, response.ScannedCount);
    }

    [Fact]
    public void LimitCapsResultsAndSaysSo()
    {
        var response = Find(Request() with { Limit = 2 });

        Assert.Equal(2, response.Count);
        Assert.True(response.Truncated);
        Assert.Equal(3, response.MatchedCount);
    }

    [Fact]
    public void TruncationFromTheSweepIsCarriedThrough()
    {
        var response = Find(Request(), sweepTruncated: true);

        // The drawing was bigger than the sweep cap, so the answer is a prefix
        // and the caller has to know that before concluding anything from it.
        Assert.True(response.Truncated);
    }

    [Fact]
    public void TheCallerCanSetTheScanBudget()
    {
        var adapter = new FakeSweepAdapter(false);

        new CivilReadService().FindPropertySets(adapter, Document(), Request() with { MaxScan = 120_000 });

        // Live validation hit the old fixed ceiling on a real drawing, so the
        // budget belongs to the caller, who is the one waiting on Civil 3D.
        Assert.Equal(120_000, adapter.RequestedBudget);
    }

    [Fact]
    public void ANonPositiveScanBudgetIsRejected()
    {
        var error = Assert.Throws<CivilToolException>(() => Find(Request() with { MaxScan = 0 }));

        Assert.Equal(ErrorCode.InvalidRequest, error.Code);
    }

    [Fact]
    public void RequiresASetName()
    {
        var error = Assert.Throws<CivilToolException>(() => Find(Request() with { SetName = "  " }));

        Assert.Equal(ErrorCode.InvalidRequest, error.Code);
    }

    [Fact]
    public void AFilterWithoutACriterionIsRejected()
    {
        var error = Assert.Throws<CivilToolException>(() => Find(Request() with
        {
            Match = [new PropertySetValueFilter { Name = "Secao" }]
        }));

        // A filter that constrains nothing would silently widen the result.
        Assert.Equal(ErrorCode.InvalidRequest, error.Code);
    }

    [Fact]
    public void CatalogPublishesFindAsARead()
    {
        var tool = Assert.Single(CivilToolCatalog.ListTools(), descriptor => descriptor.Name == "propertyset_find");

        Assert.Equal(OperationClass.Read, tool.OperationClass);
        Assert.Equal(ToolRisk.Low, tool.Risk);
        Assert.False(tool.RequiresConfirmation);
        Assert.Equal("Civil3D", tool.Module);
    }

    private static PropertySetFindResponse Find(PropertySetFindRequest request, bool sweepTruncated = false) =>
        new CivilReadService().FindPropertySets(
            new FakeSweepAdapter(sweepTruncated),
            Document(),
            request);

    private static PropertySetFindRequest Request() =>
        new()
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            SetName = SetName
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

    private sealed class FakeSweepAdapter : IPropertySetSweepAdapter
    {
        private readonly bool _truncated;

        public FakeSweepAdapter(bool truncated)
        {
            _truncated = truncated;
        }

        public int? RequestedBudget { get; private set; }

        public PropertySetSweep SweepPropertySets(string setName, int? maxScan)
        {
            RequestedBudget = maxScan;
            return Sweep();
        }

        private PropertySetSweep Sweep() =>
            new(
            [
                Carrier("A1", "529", "km-529"),
                Carrier("B2", "530", "km-530"),
                Carrier("C3", "531", "km-531")
            ],
            ScannedCount: 4146,
            Truncated: _truncated);

        private static PropertySetCarrier Carrier(string handle, string secao, string pontoA) =>
            new(handle, "AcDbBlockReference", new PropertySetInfo
            {
                Name = SetName,
                Handle = handle + "S",
                DefinitionHandle = "DEF",
                PropertyCount = 3,
                Properties =
                [
                    new PropertyValueInfo { Name = "Secao", DataType = "Text", Value = secao },
                    new PropertyValueInfo { Name = "Ponto_A", DataType = "Text", Value = pontoA },
                    new PropertyValueInfo { Name = "BR", DataType = "Text", Value = "153" }
                ]
            });
    }
}
