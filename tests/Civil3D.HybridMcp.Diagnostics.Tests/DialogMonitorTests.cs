using Civil3D.HybridMcp.Diagnostics;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Diagnostics.Tests;

public sealed class DialogMonitorTests
{
    private const long MainWindow = 0x100;
    private const long ModalDialog = 0x200;
    private const long PalettePanel = 0x300;

    [Fact]
    public void DetectsModalWhenMainWindowIsDisabledAndOwnedDialogIsEnabled()
    {
        var service = new DialogMonitorService();
        var enumerator = new FakeWindowEnumerator(
            4242,
            [
                Window(MainWindow, "Autodesk Civil 3D 2026 - [Projeto.dwg]", "AcadFrame", isEnabled: false, owner: 0, area: 2_000_000),
                Window(ModalDialog, "References - Not Found Files", "#32770", isEnabled: true, owner: MainWindow, area: 160_000)
            ]);

        var response = service.List("instance-1", enumerator);

        Assert.Equal("instance-1", response.InstanceId);
        Assert.Equal(4242, response.ProcessId);
        Assert.True(response.BlockedByModal);
        Assert.Equal("References - Not Found Files", response.BlockingDialogTitle);
        Assert.False(response.MainWindowEnabled);
        Assert.Equal("Autodesk Civil 3D 2026 - [Projeto.dwg]", response.MainWindowTitle);
        Assert.Equal(1, response.Count);

        var dialog = Assert.Single(response.Dialogs);
        Assert.Equal("References - Not Found Files", dialog.Title);
        Assert.Equal("#32770", dialog.ClassName);
        Assert.True(dialog.IsModalCandidate);
    }

    [Fact]
    public void ReportsNoModalWhenMainWindowIsEnabled()
    {
        var service = new DialogMonitorService();
        var enumerator = new FakeWindowEnumerator(
            4242,
            [
                Window(MainWindow, "Autodesk Civil 3D 2026", "AcadFrame", isEnabled: true, owner: 0, area: 2_000_000),
                Window(PalettePanel, "Properties", "AcadPalette", isEnabled: true, owner: MainWindow, area: 90_000)
            ]);

        var response = service.List("instance-1", enumerator);

        Assert.False(response.BlockedByModal);
        Assert.Null(response.BlockingDialogTitle);
        Assert.True(response.MainWindowEnabled);
        Assert.Equal(1, response.Count);
        Assert.False(response.Dialogs[0].IsModalCandidate);
    }

    [Fact]
    public void PicksLargestUnownedVisibleWindowAsMainWindow()
    {
        var service = new DialogMonitorService();
        var enumerator = new FakeWindowEnumerator(
            7,
            [
                Window(0x900, "Hidden helper", "Helper", isEnabled: true, owner: 0, area: 8_000_000, isVisible: false),
                Window(0x901, "Small tool window", "Tool", isEnabled: true, owner: 0, area: 10_000),
                Window(MainWindow, "Autodesk Civil 3D 2026", "AcadFrame", isEnabled: true, owner: 0, area: 2_000_000)
            ]);

        var response = service.List("instance-1", enumerator);

        Assert.Equal("Autodesk Civil 3D 2026", response.MainWindowTitle);
        Assert.Equal("0x0000000000000100", response.MainWindowHandle);
    }

    [Fact]
    public void IgnoresInvisibleAndUntitledWindows()
    {
        var service = new DialogMonitorService();
        var enumerator = new FakeWindowEnumerator(
            7,
            [
                Window(MainWindow, "Autodesk Civil 3D 2026", "AcadFrame", isEnabled: false, owner: 0, area: 2_000_000),
                Window(0x201, "   ", "#32770", isEnabled: true, owner: MainWindow, area: 1000),
                Window(0x202, "Invisible dialog", "#32770", isEnabled: true, owner: MainWindow, area: 1000, isVisible: false),
                Window(ModalDialog, "Drawing Recovery", "#32770", isEnabled: true, owner: MainWindow, area: 120_000)
            ]);

        var response = service.List("instance-1", enumerator);

        Assert.Equal(1, response.Count);
        Assert.Equal("Drawing Recovery", response.Dialogs[0].Title);
        Assert.True(response.BlockedByModal);
    }

    [Fact]
    public void HandlesMissingMainWindowWithoutThrowing()
    {
        var service = new DialogMonitorService();
        var enumerator = new FakeWindowEnumerator(7, []);

        var response = service.List("instance-1", enumerator);

        Assert.False(response.BlockedByModal);
        Assert.Null(response.MainWindowHandle);
        Assert.Null(response.MainWindowTitle);
        Assert.Equal(0, response.Count);
        Assert.NotEmpty(response.Limitations);
    }

    [Fact]
    public void SurfacesEnumerationFailureAsLimitationInsteadOfThrowing()
    {
        var service = new DialogMonitorService();

        var response = service.List("instance-1", new ThrowingWindowEnumerator());

        Assert.False(response.BlockedByModal);
        Assert.Equal(0, response.Count);
        Assert.Contains(response.Limitations, limitation => limitation.Contains("enumerate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StatusReportsBlockingDialogForCivil3dStatus()
    {
        var service = new DialogMonitorService();
        var enumerator = new FakeWindowEnumerator(
            4242,
            [
                Window(MainWindow, "Autodesk Civil 3D 2026", "AcadFrame", isEnabled: false, owner: 0, area: 2_000_000),
                Window(ModalDialog, "References - Not Found Files", "#32770", isEnabled: true, owner: MainWindow, area: 160_000)
            ]);

        var status = service.GetStatus(enumerator);

        Assert.True(status.Supported);
        Assert.Equal("active", status.State);
        Assert.True(status.HasBlockingDialog);
        Assert.Equal(1, status.DialogCount);
        Assert.Equal("References - Not Found Files", status.BlockingDialogTitle);
    }

    [Fact]
    public void StatusStaysSupportedWhenNothingIsBlocking()
    {
        var service = new DialogMonitorService();
        var enumerator = new FakeWindowEnumerator(
            4242,
            [
                Window(MainWindow, "Autodesk Civil 3D 2026", "AcadFrame", isEnabled: true, owner: 0, area: 2_000_000)
            ]);

        var status = service.GetStatus(enumerator);

        Assert.True(status.Supported);
        Assert.Equal("active", status.State);
        Assert.False(status.HasBlockingDialog);
        Assert.Equal(0, status.DialogCount);
        Assert.Null(status.BlockingDialogTitle);
    }

    [Fact]
    public void DialogMonitorIsWiredThroughTheDiagnosticsInterface()
    {
        var enumerator = new FakeWindowEnumerator(
            4242,
            [
                Window(MainWindow, "Autodesk Civil 3D 2026", "AcadFrame", isEnabled: false, owner: 0, area: 2_000_000),
                Window(ModalDialog, "References - Not Found Files", "#32770", isEnabled: true, owner: MainWindow, area: 160_000)
            ]);
        IDialogMonitor monitor = new WindowDialogMonitor(enumerator);

        var status = monitor.GetStatus();

        Assert.True(status.HasBlockingDialog);
        Assert.Equal("References - Not Found Files", status.BlockingDialogTitle);
    }

    private static WindowSnapshot Window(
        long handle,
        string? title,
        string className,
        bool isEnabled,
        long owner,
        long area,
        bool isVisible = true) =>
        new()
        {
            Handle = handle,
            Title = title,
            ClassName = className,
            IsVisible = isVisible,
            IsEnabled = isEnabled,
            OwnerHandle = owner,
            Area = area
        };

    private sealed class FakeWindowEnumerator : IWindowEnumerator
    {
        private readonly IReadOnlyList<WindowSnapshot> _windows;

        public FakeWindowEnumerator(int processId, IReadOnlyList<WindowSnapshot> windows)
        {
            ProcessId = processId;
            _windows = windows;
        }

        public int ProcessId { get; }

        public IReadOnlyList<WindowSnapshot> EnumerateTopLevelWindows() => _windows;

        // These tests are about modal classification, not dialog contents.
        public IReadOnlyList<ControlSnapshot> EnumerateChildControls(long windowHandle) => [];
    }

    private sealed class ThrowingWindowEnumerator : IWindowEnumerator
    {
        public int ProcessId => 1;

        public IReadOnlyList<WindowSnapshot> EnumerateTopLevelWindows() =>
            throw new InvalidOperationException("could not enumerate windows");

        public IReadOnlyList<ControlSnapshot> EnumerateChildControls(long windowHandle) =>
            throw new InvalidOperationException("could not enumerate child controls");
    }
}
