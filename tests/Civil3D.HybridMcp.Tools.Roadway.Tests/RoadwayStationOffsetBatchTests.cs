using Civil3D.HybridMcp.Protocol;
using Civil3D.HybridMcp.Tools.Roadway;

namespace Civil3D.HybridMcp.Tools.Roadway.Tests;

/// <summary>
/// Phase 6A: the first roadway tool.
///
/// A survey crew hands over a list of points and someone types each one into
/// Civil 3D to read its station and offset. This does the whole list in one
/// call, over the alignment the caller names.
///
/// The rule that shapes everything here is that a batch must not be
/// all-or-nothing. A point that falls outside the alignment is a normal result
/// in roadway work, not a failure of the other forty points, so each row carries
/// its own outcome and the caller can still use the rest.
/// </summary>
public sealed class RoadwayStationOffsetBatchTests
{
    private const string Alignment = "BR-153";

    [Fact]
    public void ResolvesEveryPointAndKeepsRequestOrder()
    {
        var service = new RoadwayStationOffsetService();
        var adapter = new FakeRoadwayAlignmentAdapter();

        var response = service.StationOffsetBatch(adapter, Document(), Request(
            Point(684_800, 7_552_450),
            Point(684_900, 7_552_460),
            Point(685_000, 7_552_470)));

        Assert.Equal(3, response.Count);
        Assert.Equal(3, response.ResolvedCount);
        Assert.Equal(0, response.FailedCount);
        Assert.Collection(
            response.Points,
            point => Assert.Equal(0, point.Index),
            point => Assert.Equal(1, point.Index),
            point => Assert.Equal(2, point.Index));
        Assert.All(response.Points, point => Assert.True(point.Resolved));
    }

    [Fact]
    public void APointOutsideTheAlignmentDoesNotFailTheOtherPoints()
    {
        var service = new RoadwayStationOffsetService();
        var adapter = new FakeRoadwayAlignmentAdapter { FailingIndexes = [1] };

        var response = service.StationOffsetBatch(adapter, Document(), Request(
            Point(684_800, 7_552_450),
            Point(0, 0),
            Point(685_000, 7_552_470)));

        Assert.Equal(3, response.Count);
        Assert.Equal(2, response.ResolvedCount);
        Assert.Equal(1, response.FailedCount);

        var failed = response.Points[1];
        Assert.False(failed.Resolved);
        Assert.Null(failed.Station);
        Assert.Null(failed.Offset);
        Assert.NotNull(failed.Error);

        // The rows around the failure are still usable.
        Assert.True(response.Points[0].Resolved);
        Assert.True(response.Points[2].Resolved);
    }

    [Fact]
    public void EveryRowCarriesTheRequestedPointBackForMatching()
    {
        var service = new RoadwayStationOffsetService();
        var adapter = new FakeRoadwayAlignmentAdapter();

        var response = service.StationOffsetBatch(adapter, Document(), Request(Point(684_800, 7_552_450)));

        var point = Assert.Single(response.Points);
        Assert.Equal(684_800, point.X);
        Assert.Equal(7_552_450, point.Y);
        Assert.Equal(Alignment, response.Alignment);
    }

    [Fact]
    public void AnUnknownAlignmentFailsOnceRatherThanPerPoint()
    {
        var service = new RoadwayStationOffsetService();
        var adapter = new FakeRoadwayAlignmentAdapter { AlignmentExists = false };

        var error = Assert.Throws<RoadwayToolException>(
            () => service.StationOffsetBatch(adapter, Document(), Request(Point(1, 1), Point(2, 2))));

        // Naming a missing alignment is a caller mistake, not forty bad points.
        Assert.Equal(ErrorCode.AlignmentNotFound, error.Code);
        Assert.Equal(0, adapter.Calls);
    }

    [Fact]
    public void RequiresAnAlignmentAndAtLeastOnePoint()
    {
        var service = new RoadwayStationOffsetService();
        var adapter = new FakeRoadwayAlignmentAdapter();

        Assert.Equal(
            ErrorCode.InvalidRequest,
            Assert.Throws<RoadwayToolException>(
                () => service.StationOffsetBatch(adapter, Document(), Request() with { Alignment = "  " })).Code);

        Assert.Equal(
            ErrorCode.InvalidRequest,
            Assert.Throws<RoadwayToolException>(
                () => service.StationOffsetBatch(adapter, Document(), Request())).Code);
    }

    [Fact]
    public void RejectsABatchLargerThanTheCapInsteadOfStallingTheHost()
    {
        var service = new RoadwayStationOffsetService();
        var adapter = new FakeRoadwayAlignmentAdapter();
        var points = Enumerable.Range(0, RoadwayStationOffsetService.MaxPoints + 1)
            .Select(index => Point(index, index))
            .ToArray();

        var error = Assert.Throws<RoadwayToolException>(
            () => service.StationOffsetBatch(adapter, Document(), Request(points)));

        // This runs on the Autodesk idle loop; an unbounded batch would freeze
        // the application the operator is sitting in front of.
        Assert.Equal(ErrorCode.InvalidRequest, error.Code);
        Assert.Contains(RoadwayStationOffsetService.MaxPoints.ToString(), error.Message);
        Assert.Equal(0, adapter.Calls);
    }

    [Fact]
    public void RejectsAForeignDocumentTarget()
    {
        var service = new RoadwayStationOffsetService();

        var error = Assert.Throws<RoadwayToolException>(() => service.StationOffsetBatch(
            new FakeRoadwayAlignmentAdapter(),
            Document(),
            Request(Point(1, 1)) with { DocumentId = "doc-9" }));

        Assert.Equal(ErrorCode.DocumentNotFound, error.Code);
    }

    [Fact]
    public void CatalogPublishesTheBatchAsAReadTool()
    {
        var tool = Assert.Single(RoadwayToolCatalog.ListTools(), descriptor => descriptor.Name == "roadway_station_offset_batch");

        Assert.Equal(OperationClass.Read, tool.OperationClass);
        Assert.Equal(ToolRisk.Low, tool.Risk);
        Assert.False(tool.RequiresConfirmation);
        Assert.Equal(RollbackSemantics.NoneRequired, tool.RollbackSemantics);
        Assert.Equal("Roadway", tool.Module);
    }

    private static RoadwayPointInput Point(double x, double y) => new() { X = x, Y = y };

    private static RoadwayStationOffsetBatchRequest Request(params RoadwayPointInput[] points) =>
        new()
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Alignment = Alignment,
            Points = points
        };

    private static DocumentDescriptor Document() =>
        new()
        {
            InstanceId = "instance-1",
            DocumentId = "doc-1",
            Name = "BR-153.dwg",
            FullPath = null,
            IsActive = true,
            IsSaved = true,
            IsReadOnly = false,
            Units = "Meters",
            DatabaseFingerprint = "fingerprint-1",
            SessionIdentifier = "session-1"
        };

    private sealed class FakeRoadwayAlignmentAdapter : IRoadwayAlignmentAdapter
    {
        public bool AlignmentExists { get; init; } = true;

        public IReadOnlyList<int> FailingIndexes { get; init; } = [];

        public int Calls { get; private set; }

        public bool AlignmentFound(string alignment) => AlignmentExists;

        public RoadwayStationOffsetResult StationOffset(string alignment, double x, double y)
        {
            var index = Calls++;
            return FailingIndexes.Contains(index)
                ? new RoadwayStationOffsetResult(false, null, null, "Point is outside the alignment range.")
                : new RoadwayStationOffsetResult(true, 1000 + index, 5.5, null);
        }
    }
}
