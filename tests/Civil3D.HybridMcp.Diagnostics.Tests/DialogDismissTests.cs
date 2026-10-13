using Civil3D.HybridMcp.Diagnostics;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Diagnostics.Tests;

/// <summary>
/// Phase 1F: pressing the button, at last.
///
/// This is the first tool in the platform that acts on the host UI, so almost
/// every test here is about a refusal. The tool presses exactly one button, the
/// one a person named for that exact dialog, and does nothing at all in every
/// other situation.
///
/// The one success case is deliberately narrow: a decision exists, the dialog is
/// unambiguous, its contents were read, and the named button is present and
/// enabled. Anything short of that leaves the dialog on screen.
/// </summary>
public sealed class DialogDismissTests
{
    private const string Title = "References - Not Found Files";
    private const string Ignore = "Ignore unresolved reference files";
    private const string Update = "Update from server";

    [Fact]
    public void PressesTheDecidedButtonAndReportsWhatItPressed()
    {
        var harness = Harness.WithDialog(Title, Ignore, Update);
        harness.Decisions.Record(Decision(Ignore, DialogDecisionScope.Default));

        var response = harness.Dismiss(new DialogDismissRequest
        {
            InstanceId = "instance-1",
            ExpectedTitle = Title
        });

        Assert.True(response.Pressed);
        Assert.Equal(DialogDecisionOutcome.Resolved, response.Outcome);
        Assert.Equal(Ignore, response.ButtonText);
        Assert.Equal(DialogDecisionScope.Default, response.Scope);
        var press = Assert.Single(harness.Actuator.Presses);
        Assert.Equal(Ignore, press.Caption);
    }

    [Fact]
    public void WithoutADecisionNothingIsPressedAndTheOptionsComeBack()
    {
        var harness = Harness.WithDialog(Title, Ignore, Update);

        var response = harness.Dismiss(new DialogDismissRequest
        {
            InstanceId = "instance-1",
            ExpectedTitle = Title
        });

        Assert.False(response.Pressed);
        Assert.Equal(DialogDecisionOutcome.DecisionRequired, response.Outcome);
        Assert.Empty(harness.Actuator.Presses);
        Assert.Collection(
            response.Options,
            option => Assert.Equal(Ignore, option.Text),
            option => Assert.Equal(Update, option.Text));
    }

    [Fact]
    public void ADecisionCanBeMadeAndAppliedInTheSameCall()
    {
        var harness = Harness.WithDialog(Title, Ignore, Update);

        var response = harness.Dismiss(new DialogDismissRequest
        {
            InstanceId = "instance-1",
            ExpectedTitle = Title,
            ButtonText = Ignore,
            Scope = DialogDecisionScope.Once
        });

        Assert.True(response.Pressed);
        Assert.Equal(Ignore, response.ButtonText);
        Assert.Equal(DialogDecisionScope.Once, response.Scope);
        Assert.Single(harness.Actuator.Presses);
    }

    [Fact]
    public void AOneTimeDecisionIsSpentSoTheNextOccurrenceAsksAgain()
    {
        var harness = Harness.WithDialog(Title, Ignore, Update);
        var request = new DialogDismissRequest
        {
            InstanceId = "instance-1",
            ExpectedTitle = Title,
            ButtonText = Ignore,
            Scope = DialogDecisionScope.Once
        };

        var first = harness.Dismiss(request);
        var second = harness.Dismiss(new DialogDismissRequest
        {
            InstanceId = "instance-1",
            ExpectedTitle = Title
        });

        Assert.True(first.Pressed);
        Assert.False(second.Pressed);
        Assert.Equal(DialogDecisionOutcome.DecisionRequired, second.Outcome);
        Assert.Single(harness.Actuator.Presses);
    }

    [Fact]
    public void ADefaultDecisionKeepsWorkingOnTheNextOccurrence()
    {
        var harness = Harness.WithDialog(Title, Ignore, Update);
        harness.Decisions.Record(Decision(Ignore, DialogDecisionScope.Default));
        var request = new DialogDismissRequest { InstanceId = "instance-1", ExpectedTitle = Title };

        harness.Dismiss(request);
        var second = harness.Dismiss(request);

        Assert.True(second.Pressed);
        Assert.Equal(2, harness.Actuator.Presses.Count);
    }

    [Fact]
    public void ADialogWithADifferentTitleIsNeverTouched()
    {
        var harness = Harness.WithDialog("Drawing Recovery", "Close");
        harness.Decisions.Record(Decision(Ignore, DialogDecisionScope.Default));

        var error = Assert.Throws<DialogToolException>(() => harness.Dismiss(new DialogDismissRequest
        {
            InstanceId = "instance-1",
            ExpectedTitle = Title
        }));

        Assert.Equal(ErrorCode.DialogNotFound, error.Code);
        Assert.Empty(harness.Actuator.Presses);
    }

    [Fact]
    public void TwoDialogsWithTheSameTitleAreRefusedRatherThanGuessed()
    {
        var harness = Harness.WithTwoDialogs(Title, Ignore);
        harness.Decisions.Record(Decision(Ignore, DialogDecisionScope.Default));

        var error = Assert.Throws<DialogToolException>(() => harness.Dismiss(new DialogDismissRequest
        {
            InstanceId = "instance-1",
            ExpectedTitle = Title
        }));

        Assert.Equal(ErrorCode.DialogAmbiguous, error.Code);
        Assert.Empty(harness.Actuator.Presses);
    }

    [Fact]
    public void AnExpectedTitleIsRequired()
    {
        var harness = Harness.WithDialog(Title, Ignore);

        var error = Assert.Throws<DialogToolException>(() => harness.Dismiss(new DialogDismissRequest
        {
            InstanceId = "instance-1",
            ExpectedTitle = "   "
        }));

        Assert.Equal(ErrorCode.InvalidRequest, error.Code);
        Assert.Empty(harness.Actuator.Presses);
    }

    [Fact]
    public void ADecidedButtonThatIsGoneIsNeverSubstituted()
    {
        var harness = Harness.WithDialog(Title, Update, "Cancel");
        harness.Decisions.Record(Decision(Ignore, DialogDecisionScope.Default));

        var response = harness.Dismiss(new DialogDismissRequest
        {
            InstanceId = "instance-1",
            ExpectedTitle = Title
        });

        Assert.False(response.Pressed);
        Assert.Equal(DialogDecisionOutcome.ButtonNotPresent, response.Outcome);
        Assert.Empty(harness.Actuator.Presses);
    }

    [Fact]
    public void AnUnreadableDialogIsLeftAlone()
    {
        var harness = Harness.WithUnreadableDialog(Title);
        harness.Decisions.Record(Decision(Ignore, DialogDecisionScope.Default));

        var response = harness.Dismiss(new DialogDismissRequest
        {
            InstanceId = "instance-1",
            ExpectedTitle = Title
        });

        Assert.False(response.Pressed);
        Assert.Equal(DialogDecisionOutcome.ContentUnreadable, response.Outcome);
        Assert.Empty(harness.Actuator.Presses);
    }

    [Fact]
    public void AFailedPressIsReportedRatherThanAssumedToHaveWorked()
    {
        var harness = Harness.WithDialog(Title, Ignore);
        harness.Actuator.Succeed = false;
        harness.Decisions.Record(Decision(Ignore, DialogDecisionScope.Default));

        var error = Assert.Throws<DialogToolException>(() => harness.Dismiss(new DialogDismissRequest
        {
            InstanceId = "instance-1",
            ExpectedTitle = Title
        }));

        Assert.Equal(ErrorCode.DialogDismissFailed, error.Code);
    }

    [Fact]
    public void AClickThatLeavesTheDialogOpenIsReportedAsFailure()
    {
        var harness = Harness.WithDialog(Title, Ignore);
        harness.Actuator.ClosesAfterPress = false;
        harness.Decisions.Record(Decision(Ignore, DialogDecisionScope.Default));

        var error = Assert.Throws<DialogToolException>(() => harness.Dismiss(new DialogDismissRequest
        {
            InstanceId = "instance-1",
            ExpectedTitle = Title
        }));

        // Posting a click only queues a message. Reporting success on that alone
        // would tell the operator the host is unblocked when it is not.
        Assert.Equal(ErrorCode.DialogDismissFailed, error.Code);
        Assert.Single(harness.Actuator.Presses);
    }

    private static DialogDecision Decision(string button, DialogDecisionScope scope) =>
        new()
        {
            DialogTitle = Title,
            ButtonText = button,
            Scope = scope
        };

    private sealed class Harness
    {
        public required FakeDismissEnumerator Enumerator { get; init; }
        public required FakeActuator Actuator { get; init; }
        public required DialogDecisionStore Decisions { get; init; }

        public DialogDismissResponse Dismiss(DialogDismissRequest request) =>
            new DialogMonitorService().Dismiss(request, Enumerator, Actuator, Decisions);

        public static Harness WithDialog(string title, params string[] buttons) =>
            Build([Window(20, title)], new Dictionary<long, IReadOnlyList<ControlSnapshot>>
            {
                [20] = buttons.Select((caption, index) => Control(index + 1, caption)).ToArray()
            });

        public static Harness WithTwoDialogs(string title, string button) =>
            Build([Window(20, title), Window(21, title)], new Dictionary<long, IReadOnlyList<ControlSnapshot>>
            {
                [20] = [Control(1, button)],
                [21] = [Control(1, button)]
            });

        public static Harness WithUnreadableDialog(string title) =>
            Build([Window(20, title)], new Dictionary<long, IReadOnlyList<ControlSnapshot>>(), failing: [20]);

        private static Harness Build(
            IReadOnlyList<WindowSnapshot> dialogs,
            IReadOnlyDictionary<long, IReadOnlyList<ControlSnapshot>> controls,
            IReadOnlyList<long>? failing = null)
        {
            var windows = new List<WindowSnapshot>
            {
                new()
                {
                    Handle = 10,
                    Title = "Autodesk Civil 3D 2026",
                    ClassName = "Afx:main",
                    IsVisible = true,
                    IsEnabled = false,
                    OwnerHandle = 0,
                    Area = 1_000_000
                }
            };
            windows.AddRange(dialogs);

            return new Harness
            {
                Enumerator = new FakeDismissEnumerator(windows, controls, failing),
                Actuator = new FakeActuator(controls),
                Decisions = new DialogDecisionStore(new NullDecisionRepository())
            };
        }

        private static WindowSnapshot Window(long handle, string title) =>
            new()
            {
                Handle = handle,
                Title = title,
                ClassName = "#32770",
                IsVisible = true,
                IsEnabled = true,
                OwnerHandle = 10,
                Area = 40_000
            };

        private static ControlSnapshot Control(int controlId, string caption) =>
            new()
            {
                Handle = 1000 + controlId,
                ControlId = controlId,
                ClassName = "Button",
                Text = caption,
                IsVisible = true,
                IsEnabled = true
            };
    }

    private sealed class FakeDismissEnumerator : IWindowEnumerator
    {
        private readonly IReadOnlyList<WindowSnapshot> _windows;
        private readonly IReadOnlyDictionary<long, IReadOnlyList<ControlSnapshot>> _controls;
        private readonly HashSet<long> _failing;

        public FakeDismissEnumerator(
            IReadOnlyList<WindowSnapshot> windows,
            IReadOnlyDictionary<long, IReadOnlyList<ControlSnapshot>> controls,
            IReadOnlyList<long>? failing)
        {
            _windows = windows;
            _controls = controls;
            _failing = failing is null ? [] : [.. failing];
        }

        public int ProcessId => 4242;

        public IReadOnlyList<WindowSnapshot> EnumerateTopLevelWindows() => _windows;

        public IReadOnlyList<ControlSnapshot> EnumerateChildControls(long windowHandle)
        {
            if (_failing.Contains(windowHandle))
            {
                throw new InvalidOperationException("could not enumerate child controls");
            }

            return _controls.TryGetValue(windowHandle, out var controls) ? controls : [];
        }
    }

    private sealed class FakeActuator : IDialogActuator
    {
        private readonly IReadOnlyDictionary<long, IReadOnlyList<ControlSnapshot>> _controls;

        public FakeActuator(IReadOnlyDictionary<long, IReadOnlyList<ControlSnapshot>> controls)
        {
            _controls = controls;
        }

        public bool Succeed { get; set; } = true;

        public bool ClosesAfterPress { get; set; } = true;

        public List<(long DialogHandle, long ButtonHandle, string? Caption)> Presses { get; } = [];

        public bool PressButton(long dialogHandle, long buttonHandle, int controlId)
        {
            var caption = _controls.TryGetValue(dialogHandle, out var controls)
                ? controls.FirstOrDefault(control => control.Handle == buttonHandle)?.Text
                : null;

            if (!Succeed)
            {
                return false;
            }

            Presses.Add((dialogHandle, buttonHandle, caption));
            return true;
        }

        public bool WaitUntilClosed(long dialogHandle, TimeSpan timeout) => ClosesAfterPress;
    }

    private sealed class NullDecisionRepository : IDialogDecisionRepository
    {
        public IReadOnlyList<DialogDecision> Load() => [];

        public void Save(IReadOnlyList<DialogDecision> decisions)
        {
        }
    }
}
