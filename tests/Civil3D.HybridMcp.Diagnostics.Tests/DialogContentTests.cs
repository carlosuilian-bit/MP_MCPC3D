using Civil3D.HybridMcp.Diagnostics;

namespace Civil3D.HybridMcp.Diagnostics.Tests;

/// <summary>
/// Phase 1D: reading what a blocking dialog actually says.
///
/// `list_dialogs` could name the dialog but not its message or its buttons, and
/// that gap is what blocks `dismiss_dialog`: an allowlist cannot be designed,
/// let alone enforced, against a button whose caption nobody can read.
///
/// The rules under test are about cost and honesty. Contents are read only for
/// windows already classified as dialogs, never for the main frame, because this
/// tool runs when the host is hung. A dialog whose children cannot be read says
/// so instead of reporting an empty button list that looks like a dialog with no
/// buttons.
/// </summary>
public sealed class DialogContentTests
{
    [Fact]
    public void ReadsMessageTextAndButtonsOfABlockingDialog()
    {
        var enumerator = new FakeContentEnumerator(
            Windows(MainWindow(enabled: false), Dialog(handle: 20)),
            new Dictionary<long, IReadOnlyList<ControlSnapshot>>
            {
                [20] =
                [
                    Control(1, "Static", "Some reference files could not be located."),
                    Control(2, "Static", "What do you want to do?"),
                    Control(3, "Button", "Ignore unresolved reference files"),
                    Control(4, "Button", "Update from server")
                ]
            });

        var response = new DialogMonitorService().List("instance-1", enumerator);
        var dialog = Assert.Single(response.Dialogs);

        Assert.True(dialog.ContentRead);
        Assert.Equal(
            "Some reference files could not be located. What do you want to do?",
            dialog.Text);
        Assert.Collection(
            dialog.Buttons,
            button => Assert.Equal("Ignore unresolved reference files", button.Text),
            button => Assert.Equal("Update from server", button.Text));
        Assert.Equal(3, dialog.Buttons[0].ControlId);
        Assert.True(dialog.Buttons[0].IsEnabled);
    }

    [Fact]
    public void IgnoresControlsThatAreNeitherButtonsNorText()
    {
        var enumerator = new FakeContentEnumerator(
            Windows(MainWindow(enabled: false), Dialog(handle: 20)),
            new Dictionary<long, IReadOnlyList<ControlSnapshot>>
            {
                [20] =
                [
                    Control(1, "Static", "message"),
                    Control(2, "SysListView32", "a list nobody can act on"),
                    Control(3, "Edit", "typed text"),
                    Control(4, "Button", "OK")
                ]
            });

        var dialog = Assert.Single(new DialogMonitorService().List("instance-1", enumerator).Dialogs);

        Assert.Equal("message", dialog.Text);
        Assert.Equal("OK", Assert.Single(dialog.Buttons).Text);
    }

    [Fact]
    public void SkipsButtonsAndTextWithNoCaption()
    {
        var enumerator = new FakeContentEnumerator(
            Windows(MainWindow(enabled: false), Dialog(handle: 20)),
            new Dictionary<long, IReadOnlyList<ControlSnapshot>>
            {
                [20] =
                [
                    Control(1, "Static", "   "),
                    Control(2, "Static", null),
                    Control(3, "Button", ""),
                    Control(4, "Button", "Close")
                ]
            });

        var dialog = Assert.Single(new DialogMonitorService().List("instance-1", enumerator).Dialogs);

        Assert.Null(dialog.Text);
        Assert.Equal("Close", Assert.Single(dialog.Buttons).Text);
    }

    [Fact]
    public void ReportsContentAsUnreadWhenChildEnumerationFails()
    {
        var enumerator = new FakeContentEnumerator(
            Windows(MainWindow(enabled: false), Dialog(handle: 20)),
            controls: new Dictionary<long, IReadOnlyList<ControlSnapshot>>(),
            failingHandles: [20]);

        var response = new DialogMonitorService().List("instance-1", enumerator);
        var dialog = Assert.Single(response.Dialogs);

        // An unreadable dialog must not look like a dialog with no buttons.
        Assert.False(dialog.ContentRead);
        Assert.Null(dialog.Text);
        Assert.Empty(dialog.Buttons);
        Assert.Equal("References - Not Found Files", dialog.Title);
        Assert.True(response.BlockedByModal);
    }

    [Fact]
    public void NeverReadsChildrenOfTheMainWindow()
    {
        var enumerator = new FakeContentEnumerator(
            Windows(MainWindow(enabled: false), Dialog(handle: 20)),
            new Dictionary<long, IReadOnlyList<ControlSnapshot>>
            {
                [20] = [Control(1, "Button", "OK")]
            });

        new DialogMonitorService().List("instance-1", enumerator);

        // The main frame can own thousands of controls and this tool runs while
        // the host is hung, so its children are deliberately never walked.
        Assert.DoesNotContain(10L, enumerator.Inspected);
        Assert.Contains(20L, enumerator.Inspected);
    }

    [Fact]
    public void StopsClaimingContentsAreUnreadableOnceTheyAreRead()
    {
        var enumerator = new FakeContentEnumerator(
            Windows(MainWindow(enabled: false), Dialog(handle: 20)),
            new Dictionary<long, IReadOnlyList<ControlSnapshot>>
            {
                [20] = [Control(1, "Button", "OK")]
            });

        var response = new DialogMonitorService().List("instance-1", enumerator);

        Assert.DoesNotContain(response.Limitations, limitation => limitation.Contains("button captions", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(response.Capabilities, capability => capability == "dialog_content_read");
    }

    private static IReadOnlyList<WindowSnapshot> Windows(params WindowSnapshot[] windows) => windows;

    private static WindowSnapshot MainWindow(bool enabled) =>
        new()
        {
            Handle = 10,
            Title = "Autodesk Civil 3D 2026",
            ClassName = "Afx:main",
            IsVisible = true,
            IsEnabled = enabled,
            OwnerHandle = 0,
            Area = 1_000_000
        };

    private static WindowSnapshot Dialog(long handle) =>
        new()
        {
            Handle = handle,
            Title = "References - Not Found Files",
            ClassName = "#32770",
            IsVisible = true,
            IsEnabled = true,
            OwnerHandle = 10,
            Area = 40_000
        };

    private static ControlSnapshot Control(int controlId, string className, string? text) =>
        new()
        {
            Handle = 1000 + controlId,
            ControlId = controlId,
            ClassName = className,
            Text = text,
            IsVisible = true,
            IsEnabled = true
        };

    private sealed class FakeContentEnumerator : IWindowEnumerator
    {
        private readonly IReadOnlyList<WindowSnapshot> _windows;
        private readonly IReadOnlyDictionary<long, IReadOnlyList<ControlSnapshot>> _controls;
        private readonly HashSet<long> _failingHandles;
        private readonly List<long> _inspected = [];

        public FakeContentEnumerator(
            IReadOnlyList<WindowSnapshot> windows,
            IReadOnlyDictionary<long, IReadOnlyList<ControlSnapshot>> controls,
            IReadOnlyList<long>? failingHandles = null)
        {
            _windows = windows;
            _controls = controls;
            _failingHandles = failingHandles is null ? [] : [.. failingHandles];
        }

        public int ProcessId => 4242;

        public IReadOnlyList<long> Inspected => _inspected;

        public IReadOnlyList<WindowSnapshot> EnumerateTopLevelWindows() => _windows;

        public IReadOnlyList<ControlSnapshot> EnumerateChildControls(long windowHandle)
        {
            _inspected.Add(windowHandle);
            if (_failingHandles.Contains(windowHandle))
            {
                throw new InvalidOperationException("could not enumerate child controls");
            }

            return _controls.TryGetValue(windowHandle, out var controls) ? controls : [];
        }
    }
}
