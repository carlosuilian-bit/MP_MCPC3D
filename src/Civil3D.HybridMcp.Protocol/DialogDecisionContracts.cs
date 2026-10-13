using System.Text.Json.Serialization;

namespace Civil3D.HybridMcp.Protocol;

/// <summary>
/// How long a decision about a dialog lasts.
/// </summary>
public enum DialogDecisionScope
{
    /// <summary>
    /// Stands for every future occurrence of the same dialog. Persisted, so it
    /// survives a restart of Civil 3D and of the bridge.
    /// </summary>
    Default,

    /// <summary>
    /// Good for one occurrence. Never persisted, and the operator is asked again
    /// the next time the same dialog appears.
    /// </summary>
    Once
}

/// <summary>
/// One operator decision: for this exact dialog, press this exact button.
///
/// Both halves are exact on purpose. A decision that matched loosely would
/// eventually press a button nobody chose.
/// </summary>
public sealed record DialogDecision
{
    [JsonPropertyName("dialog_title")]
    public required string DialogTitle { get; init; }

    [JsonPropertyName("button_text")]
    public required string ButtonText { get; init; }

    public required DialogDecisionScope Scope { get; init; }

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Why the operator chose this, in their own words.</summary>
    public string? Note { get; init; }
}

/// <summary>
/// Why a dialog was or was not resolved automatically.
/// </summary>
public enum DialogDecisionOutcome
{
    /// <summary>A decision applies and names a button that is present and enabled.</summary>
    Resolved,

    /// <summary>
    /// No decision applies, or the one-time decision was already used. The
    /// operator has to choose, and the options travel back with the answer.
    /// </summary>
    DecisionRequired,

    /// <summary>
    /// A decision exists but the button it names is not on the dialog, or is
    /// disabled. Nothing is pressed: a decision is about one button, not about
    /// closing the dialog by any means available.
    /// </summary>
    ButtonNotPresent,

    /// <summary>
    /// The dialog's controls could not be read, so its buttons are unknown
    /// rather than absent, and no decision can be matched against them.
    /// </summary>
    ContentUnreadable
}

public sealed record DialogDecisionResolution
{
    public required DialogDecisionOutcome Outcome { get; init; }

    public DialogDecision? Decision { get; init; }

    /// <summary>Buttons the operator can choose from when a decision is required.</summary>
    public IReadOnlyList<DialogButtonInfo> Options { get; init; } = Array.Empty<DialogButtonInfo>();

    [JsonIgnore]
    public bool Resolved => Outcome == DialogDecisionOutcome.Resolved;
}

public sealed record DialogDecisionListResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    public int Count { get; init; }

    public IReadOnlyList<DialogDecision> Decisions { get; init; } = Array.Empty<DialogDecision>();

    /// <summary>
    /// Dialogs currently on screen that have no decision yet, with the options
    /// the operator has to choose from.
    /// </summary>
    public IReadOnlyList<DialogPendingDecision> Pending { get; init; } = Array.Empty<DialogPendingDecision>();

    /// <summary>Where the persisted defaults live, so an operator can inspect or clear them.</summary>
    [JsonPropertyName("store_path")]
    public string? StorePath { get; init; }
}

public sealed record DialogPendingDecision
{
    [JsonPropertyName("dialog_title")]
    public required string DialogTitle { get; init; }

    public string? Text { get; init; }

    public required DialogDecisionOutcome Outcome { get; init; }

    public IReadOnlyList<DialogButtonInfo> Options { get; init; } = Array.Empty<DialogButtonInfo>();
}

public sealed record DialogDecisionSetRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("dialog_title")]
    public required string DialogTitle { get; init; }

    [JsonPropertyName("button_text")]
    public required string ButtonText { get; init; }

    public required DialogDecisionScope Scope { get; init; }

    public string? Note { get; init; }
}

public sealed record DialogDecisionSetResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    public required DialogDecision Decision { get; init; }

    public required bool Persisted { get; init; }

    public int Count { get; init; }
}

/// <summary>
/// Asks the platform to close one dialog by pressing one button.
///
/// <see cref="ExpectedTitle"/> is required, and it is the caller stating what
/// they believe is on screen. If nothing matches it, or more than one thing
/// does, nothing is pressed.
/// </summary>
public sealed record DialogDismissRequest
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    /// <summary>Exact title of the dialog the caller intends to close.</summary>
    [JsonPropertyName("expected_title")]
    public required string ExpectedTitle { get; init; }

    /// <summary>
    /// Decide and apply in one call. Omit both this and <see cref="Scope"/> to
    /// rely on a decision already recorded.
    /// </summary>
    [JsonPropertyName("button_text")]
    public string? ButtonText { get; init; }

    public DialogDecisionScope? Scope { get; init; }

    public string? Note { get; init; }
}

public sealed record DialogDismissResponse
{
    [JsonPropertyName("instance_id")]
    public required string InstanceId { get; init; }

    [JsonPropertyName("dialog_title")]
    public required string DialogTitle { get; init; }

    /// <summary>False whenever the platform declined to act, with the reason in <see cref="Outcome"/>.</summary>
    public required bool Pressed { get; init; }

    public required DialogDecisionOutcome Outcome { get; init; }

    [JsonPropertyName("button_text")]
    public string? ButtonText { get; init; }

    public DialogDecisionScope? Scope { get; init; }

    /// <summary>Buttons to choose from, returned whenever a decision is still needed.</summary>
    public IReadOnlyList<DialogButtonInfo> Options { get; init; } = Array.Empty<DialogButtonInfo>();

    public string? Message { get; init; }
}
