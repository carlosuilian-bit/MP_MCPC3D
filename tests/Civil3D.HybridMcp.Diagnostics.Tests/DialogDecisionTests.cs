using Civil3D.HybridMcp.Diagnostics;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Diagnostics.Tests;

/// <summary>
/// Phase 1E: the decision registry behind dialog dismissal.
///
/// A dialog is never closed because the platform guessed. It is closed because a
/// person decided, for that exact dialog and that exact button, and said whether
/// the decision stands for next time or was good only once.
///
/// The tests here are mostly about refusing to act: an absent decision, a used-up
/// one-time decision, or a button that is no longer on screen all have to leave
/// the dialog alone and hand the options back to the operator.
/// </summary>
public sealed class DialogDecisionTests
{
    private const string Title = "References - Not Found Files";
    private const string Ignore = "Ignore unresolved reference files";
    private const string Update = "Update from server";

    [Fact]
    public void WithoutADecisionTheDialogIsLeftAloneAndTheOptionsAreReturned()
    {
        var store = new DialogDecisionStore(new InMemoryDecisionRepository());

        var resolution = store.Resolve(Title, Buttons(Ignore, Update));

        Assert.False(resolution.Resolved);
        Assert.Equal(DialogDecisionOutcome.DecisionRequired, resolution.Outcome);
        Assert.Null(resolution.Decision);
        Assert.Collection(
            resolution.Options,
            option => Assert.Equal(Ignore, option.Text),
            option => Assert.Equal(Update, option.Text));
    }

    [Fact]
    public void ADefaultDecisionKeepsApplyingOnEveryOccurrence()
    {
        var store = new DialogDecisionStore(new InMemoryDecisionRepository());
        store.Record(Decision(Ignore, DialogDecisionScope.Default));

        var first = store.Resolve(Title, Buttons(Ignore, Update));
        store.Consume(first.Decision!);
        var second = store.Resolve(Title, Buttons(Ignore, Update));

        Assert.True(first.Resolved);
        Assert.Equal(Ignore, first.Decision!.ButtonText);
        Assert.True(second.Resolved);
        Assert.Equal(Ignore, second.Decision!.ButtonText);
    }

    [Fact]
    public void AOneTimeDecisionStopsApplyingAfterItIsUsed()
    {
        var store = new DialogDecisionStore(new InMemoryDecisionRepository());
        store.Record(Decision(Ignore, DialogDecisionScope.Once));

        var first = store.Resolve(Title, Buttons(Ignore, Update));
        store.Consume(first.Decision!);
        var second = store.Resolve(Title, Buttons(Ignore, Update));

        Assert.True(first.Resolved);
        Assert.False(second.Resolved);
        Assert.Equal(DialogDecisionOutcome.DecisionRequired, second.Outcome);
        Assert.Collection(
            second.Options,
            option => Assert.Equal(Ignore, option.Text),
            option => Assert.Equal(Update, option.Text));
    }

    [Fact]
    public void ADecisionWhoseButtonIsGoneNeverFallsBackToAnotherButton()
    {
        var store = new DialogDecisionStore(new InMemoryDecisionRepository());
        store.Record(Decision(Ignore, DialogDecisionScope.Default));

        var resolution = store.Resolve(Title, Buttons(Update, "Cancel"));

        // Pressing "whatever is left" is how an automation closes the wrong thing.
        Assert.False(resolution.Resolved);
        Assert.Equal(DialogDecisionOutcome.ButtonNotPresent, resolution.Outcome);
        Assert.Null(resolution.Decision);
    }

    [Fact]
    public void ADecisionForADisabledButtonIsNotApplied()
    {
        var store = new DialogDecisionStore(new InMemoryDecisionRepository());
        store.Record(Decision(Ignore, DialogDecisionScope.Default));

        var resolution = store.Resolve(Title, [Button(Ignore, enabled: false), Button(Update, enabled: true)]);

        Assert.False(resolution.Resolved);
        Assert.Equal(DialogDecisionOutcome.ButtonNotPresent, resolution.Outcome);
    }

    [Fact]
    public void ADecisionForAnotherDialogDoesNotApplyHere()
    {
        var store = new DialogDecisionStore(new InMemoryDecisionRepository());
        store.Record(Decision(Ignore, DialogDecisionScope.Default));

        var resolution = store.Resolve("Drawing Recovery", Buttons(Ignore));

        Assert.False(resolution.Resolved);
        Assert.Equal(DialogDecisionOutcome.DecisionRequired, resolution.Outcome);
    }

    [Fact]
    public void OnlyDefaultsArePersisted()
    {
        var repository = new InMemoryDecisionRepository();
        var store = new DialogDecisionStore(repository);

        store.Record(Decision(Ignore, DialogDecisionScope.Default));
        store.Record(new DialogDecision
        {
            DialogTitle = "Drawing Recovery",
            ButtonText = "Close",
            Scope = DialogDecisionScope.Once
        });

        // A one-time answer that survived a restart would stop being one-time.
        var saved = Assert.Single(repository.Saved);
        Assert.Equal(Title, saved.DialogTitle);
        Assert.Equal(DialogDecisionScope.Default, saved.Scope);
    }

    [Fact]
    public void RecordingADecisionForTheSameDialogReplacesThePreviousOne()
    {
        var store = new DialogDecisionStore(new InMemoryDecisionRepository());
        store.Record(Decision(Ignore, DialogDecisionScope.Default));
        store.Record(Decision(Update, DialogDecisionScope.Default));

        var resolution = store.Resolve(Title, Buttons(Ignore, Update));

        Assert.True(resolution.Resolved);
        Assert.Equal(Update, resolution.Decision!.ButtonText);
        Assert.Single(store.List());
    }

    [Fact]
    public void ListReportsStoredDecisionsWithTheirScope()
    {
        var store = new DialogDecisionStore(new InMemoryDecisionRepository());
        store.Record(Decision(Ignore, DialogDecisionScope.Default));
        store.Record(new DialogDecision
        {
            DialogTitle = "Drawing Recovery",
            ButtonText = "Close",
            Scope = DialogDecisionScope.Once
        });

        var decisions = store.List().OrderBy(decision => decision.DialogTitle, StringComparer.Ordinal).ToArray();

        Assert.Equal(2, decisions.Length);
        Assert.Equal("Drawing Recovery", decisions[0].DialogTitle);
        Assert.Equal(DialogDecisionScope.Once, decisions[0].Scope);
        Assert.Equal(Title, decisions[1].DialogTitle);
        Assert.Equal(DialogDecisionScope.Default, decisions[1].Scope);
    }

    [Fact]
    public void StoredDefaultsAreLoadedFromTheRepositoryOnStartup()
    {
        var repository = new InMemoryDecisionRepository();
        repository.Seed(Decision(Ignore, DialogDecisionScope.Default));

        var store = new DialogDecisionStore(repository);
        var resolution = store.Resolve(Title, Buttons(Ignore, Update));

        Assert.True(resolution.Resolved);
        Assert.Equal(Ignore, resolution.Decision!.ButtonText);
    }

    [Fact]
    public void AnUnreadableDialogCannotBeDecidedAutomatically()
    {
        var store = new DialogDecisionStore(new InMemoryDecisionRepository());
        store.Record(Decision(Ignore, DialogDecisionScope.Default));

        // content_read was false, so the button list is unknown, not empty.
        var resolution = store.Resolve(Title, options: null);

        Assert.False(resolution.Resolved);
        Assert.Equal(DialogDecisionOutcome.ContentUnreadable, resolution.Outcome);
        Assert.Empty(resolution.Options);
    }

    [Fact]
    public void PendingDecisionsCoverBlockingDialogsOnlyNotDockedPalettes()
    {
        var monitor = new WindowDialogMonitor(new PaletteEnumerator(), new InMemoryDecisionRepository());

        var response = monitor.ListDecisions("instance-1");

        // Live validation surfaced the Properties palette as something to decide
        // about. A docked palette is not a dialog being presented, and it would
        // bury the real decisions in noise.
        var pending = Assert.Single(response.Pending);
        Assert.Equal(Title, pending.DialogTitle);
    }

    private sealed class PaletteEnumerator : IWindowEnumerator
    {
        public int ProcessId => 4242;

        public IReadOnlyList<WindowSnapshot> EnumerateTopLevelWindows() =>
        [
            new()
            {
                Handle = 10,
                Title = "Autodesk Civil 3D 2026",
                ClassName = "Afx:main",
                IsVisible = true,
                IsEnabled = false,
                OwnerHandle = 0,
                Area = 1_000_000
            },
            new()
            {
                Handle = 20,
                Title = Title,
                ClassName = "#32770",
                IsVisible = true,
                IsEnabled = true,
                OwnerHandle = 10,
                Area = 40_000
            },
            new()
            {
                Handle = 30,
                Title = "Properties",
                ClassName = "Afx:palette",
                IsVisible = true,
                IsEnabled = false,
                OwnerHandle = 10,
                Area = 20_000
            }
        ];

        public IReadOnlyList<ControlSnapshot> EnumerateChildControls(long windowHandle) =>
        [
            new()
            {
                Handle = windowHandle + 1,
                ControlId = 0,
                ClassName = "Button",
                Text = windowHandle == 20 ? Ignore : "Quick Select",
                IsVisible = true,
                IsEnabled = true
            }
        ];
    }

    private static DialogDecision Decision(string button, DialogDecisionScope scope) =>
        new()
        {
            DialogTitle = Title,
            ButtonText = button,
            Scope = scope
        };

    private static IReadOnlyList<DialogButtonInfo> Buttons(params string[] captions) =>
        captions.Select(caption => Button(caption, enabled: true)).ToArray();

    private static DialogButtonInfo Button(string caption, bool enabled) =>
        new()
        {
            Text = caption,
            ControlId = caption.Length,
            IsEnabled = enabled
        };

    private sealed class InMemoryDecisionRepository : IDialogDecisionRepository
    {
        private readonly List<DialogDecision> _seeded = [];

        public List<DialogDecision> Saved { get; } = [];

        public void Seed(DialogDecision decision) => _seeded.Add(decision);

        public IReadOnlyList<DialogDecision> Load() => _seeded;

        public void Save(IReadOnlyList<DialogDecision> decisions)
        {
            Saved.Clear();
            Saved.AddRange(decisions);
        }
    }
}
