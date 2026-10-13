using System.Text.Json;
using System.Text.Json.Serialization;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Diagnostics;

public interface IDialogDecisionRepository
{
    IReadOnlyList<DialogDecision> Load();

    void Save(IReadOnlyList<DialogDecision> decisions);
}

/// <summary>
/// Holds what the operator decided about each dialog.
///
/// The whole point of this type is to say no. A dialog is dismissed only when a
/// person already chose a specific button for that specific dialog; everything
/// else hands the options back and waits. Matching is exact on both the dialog
/// title and the button caption, because a loose match eventually presses a
/// button nobody picked.
///
/// Scope is the operator's call, not a heuristic: <see cref="DialogDecisionScope.Default"/>
/// stands until it is replaced, <see cref="DialogDecisionScope.Once"/> is spent
/// after a single use and the operator is asked again.
/// </summary>
public sealed class DialogDecisionStore
{
    private readonly IDialogDecisionRepository _repository;
    private readonly Dictionary<string, DialogDecision> _decisions;
    private readonly HashSet<string> _spentOnce = new(StringComparer.Ordinal);

    public DialogDecisionStore(IDialogDecisionRepository repository)
    {
        _repository = repository;
        _decisions = repository.Load()
            .GroupBy(decision => decision.DialogTitle, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
    }

    public IReadOnlyList<DialogDecision> List() => _decisions.Values.ToArray();

    /// <summary>
    /// Records a decision, replacing any previous one for the same dialog. Only
    /// defaults reach the repository: a one-time answer that survived a restart
    /// would have stopped being one-time. The save runs either way, so replacing
    /// a stored default with a one-time answer also clears it from disk.
    /// </summary>
    public DialogDecision Record(DialogDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        _decisions[decision.DialogTitle] = decision;
        _spentOnce.Remove(decision.DialogTitle);
        Persist();
        return decision;
    }

    public bool Remove(string dialogTitle)
    {
        if (!_decisions.Remove(dialogTitle))
        {
            return false;
        }

        _spentOnce.Remove(dialogTitle);
        Persist();
        return true;
    }

    /// <param name="options">
    /// Buttons read off the live dialog, or null when the dialog's contents could
    /// not be read. Null and empty mean different things here: unknown buttons
    /// versus a dialog that genuinely offers none.
    /// </param>
    public DialogDecisionResolution Resolve(string dialogTitle, IReadOnlyList<DialogButtonInfo>? options)
    {
        if (options is null)
        {
            return new DialogDecisionResolution { Outcome = DialogDecisionOutcome.ContentUnreadable };
        }

        if (!_decisions.TryGetValue(dialogTitle, out var decision) ||
            (decision.Scope == DialogDecisionScope.Once && _spentOnce.Contains(dialogTitle)))
        {
            return new DialogDecisionResolution
            {
                Outcome = DialogDecisionOutcome.DecisionRequired,
                Options = options
            };
        }

        var match = options.FirstOrDefault(option =>
            string.Equals(option.Text, decision.ButtonText, StringComparison.Ordinal));

        if (match is null || !match.IsEnabled)
        {
            return new DialogDecisionResolution
            {
                Outcome = DialogDecisionOutcome.ButtonNotPresent,
                Options = options
            };
        }

        return new DialogDecisionResolution
        {
            Outcome = DialogDecisionOutcome.Resolved,
            Decision = decision,
            Options = options
        };
    }

    /// <summary>
    /// Marks a decision as used. A one-time decision is spent here; a default is
    /// untouched and keeps applying.
    /// </summary>
    public void Consume(DialogDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (decision.Scope == DialogDecisionScope.Once)
        {
            _spentOnce.Add(decision.DialogTitle);
        }
    }

    private void Persist() =>
        _repository.Save(_decisions.Values
            .Where(decision => decision.Scope == DialogDecisionScope.Default)
            .ToArray());
}

/// <summary>
/// File-backed decisions under the per-user platform folder, next to the
/// endpoint registry. Reading never throws: a corrupt or missing file means no
/// stored defaults, which is the safe state, not a reason to fail the tool.
/// </summary>
public sealed class FileDialogDecisionRepository : IDialogDecisionRepository
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public FileDialogDecisionRepository(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static FileDialogDecisionRepository Default()
    {
        var directory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Civil3DHybridMcp");
        return new FileDialogDecisionRepository(System.IO.Path.Combine(directory, "dialog-decisions.json"));
    }

    public IReadOnlyList<DialogDecision> Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return Array.Empty<DialogDecision>();
            }

            return JsonSerializer.Deserialize<DialogDecision[]>(File.ReadAllText(Path), Options)
                   ?? Array.Empty<DialogDecision>();
        }
        catch
        {
            // No stored defaults is the safe reading of an unreadable file: the
            // operator gets asked, rather than the platform acting on garbage.
            return Array.Empty<DialogDecision>();
        }
    }

    public void Save(IReadOnlyList<DialogDecision> decisions)
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(Path, JsonSerializer.Serialize(decisions, Options));
    }
}
