using System.Globalization;
using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Diagnostics;

/// <summary>
/// One top-level window observed in the host process. Pure data so the
/// classification logic below can be unit tested without Win32.
/// </summary>
public sealed record WindowSnapshot
{
    public required long Handle { get; init; }
    public string? Title { get; init; }
    public string? ClassName { get; init; }
    public required bool IsVisible { get; init; }
    public required bool IsEnabled { get; init; }
    public required long OwnerHandle { get; init; }
    public required long Area { get; init; }
}

/// <summary>
/// One child control of a dialog. Pure data, like <see cref="WindowSnapshot"/>,
/// so the content rules can be tested without Win32.
/// </summary>
public sealed record ControlSnapshot
{
    public required long Handle { get; init; }
    public required int ControlId { get; init; }
    public string? ClassName { get; init; }
    public string? Text { get; init; }
    public required bool IsVisible { get; init; }
    public required bool IsEnabled { get; init; }
}

/// <summary>
/// Acts on a dialog. Kept separate from <see cref="IWindowEnumerator"/> so the
/// reading path stays provably read-only: a type that can only enumerate cannot
/// press anything.
/// </summary>
public interface IDialogActuator
{
    /// <summary>Returns false when the host did not accept the click.</summary>
    bool PressButton(long dialogHandle, long buttonHandle, int controlId);

    /// <summary>
    /// Waits for the dialog to actually go away. A posted click is only queued,
    /// so without this the tool would report success for a message the dialog
    /// may have ignored.
    /// </summary>
    bool WaitUntilClosed(long dialogHandle, TimeSpan timeout);
}

public sealed class DialogToolException : Exception
{
    public DialogToolException(ErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public ErrorCode Code { get; }
}

public interface IWindowEnumerator
{
    int ProcessId { get; }

    IReadOnlyList<WindowSnapshot> EnumerateTopLevelWindows();

    /// <summary>
    /// Child controls of one window. Called only for windows already classified
    /// as dialogs: the host's main frame owns far too many controls to walk while
    /// it is hung, which is exactly when this monitor runs.
    /// </summary>
    IReadOnlyList<ControlSnapshot> EnumerateChildControls(long windowHandle);
}

/// <summary>
/// Classifies host windows into a main frame plus candidate modal dialogs.
///
/// The rule comes from how Windows implements modality: while a modal dialog is
/// up, its owner window is disabled. So a disabled main frame next to a visible,
/// enabled, owned window means the host thread is blocked.
///
/// This type performs no Win32 calls and never throws for enumeration problems;
/// it reports them as limitations instead, because this monitor is the tool an
/// operator reaches for precisely when the host is misbehaving.
/// </summary>
public sealed class DialogMonitorService
{
    public DialogListResponse List(string instanceId, IWindowEnumerator enumerator)
    {
        ArgumentNullException.ThrowIfNull(enumerator);

        var processId = SafeProcessId(enumerator);
        IReadOnlyList<WindowSnapshot> windows;
        try
        {
            windows = enumerator.EnumerateTopLevelWindows();
        }
        catch (Exception ex)
        {
            return new DialogListResponse
            {
                InstanceId = instanceId,
                ProcessId = processId,
                BlockedByModal = false,
                Count = 0,
                Capabilities = [],
                Limitations =
                [
                    $"Could not enumerate host windows: {ex.Message}",
                    "Modal state is unknown for this call; treat a hanging tool as possibly blocked."
                ]
            };
        }

        var classification = Classify(windows);
        var contents = classification.Dialogs
            .ToDictionary(window => window.Handle, window => ReadContent(enumerator, window.Handle));
        var limitations = new List<string>
        {
            "A dialog owned by a background thread of another process is not visible to this monitor."
        };

        if (contents.Values.Any(content => !content.Read))
        {
            limitations.Add("At least one dialog's controls could not be read; its buttons are unknown, not absent.");
        }

        if (classification.MainWindow is null)
        {
            limitations.Insert(0, "No visible unowned host window was found, so modal state could not be determined.");
        }

        return new DialogListResponse
        {
            InstanceId = instanceId,
            ProcessId = processId,
            MainWindowHandle = classification.MainWindow is null ? null : FormatHandle(classification.MainWindow.Handle),
            MainWindowTitle = classification.MainWindow?.Title,
            MainWindowEnabled = classification.MainWindow?.IsEnabled,
            BlockedByModal = classification.BlockedByModal,
            BlockingDialogTitle = classification.BlockingDialog?.Title,
            Count = classification.Dialogs.Count,
            Dialogs = classification.Dialogs
                .Select(window => new DialogWindowInfo
                {
                    Handle = FormatHandle(window.Handle),
                    Title = window.Title,
                    ClassName = window.ClassName,
                    IsVisible = window.IsVisible,
                    IsEnabled = window.IsEnabled,
                    IsModalCandidate = classification.BlockedByModal && window.IsEnabled,
                    Text = contents[window.Handle].Text,
                    Buttons = contents[window.Handle].Buttons,
                    ContentRead = contents[window.Handle].Read
                })
                .ToArray(),
            Capabilities =
            [
                "modal_detection",
                "host_window_enumeration",
                "dialog_content_read"
            ],
            Limitations = limitations
        };
    }

    /// <summary>
    /// Reads one dialog's controls. Button and label classes are matched by the
    /// Win32 class name rather than by position, and anything else is ignored:
    /// a list view or an edit box is not something a caller can act on, and
    /// stringifying it would only make the dialog look richer than it is.
    /// </summary>
    private static DialogContent ReadContent(IWindowEnumerator enumerator, long handle)
    {
        IReadOnlyList<ControlSnapshot> controls;
        try
        {
            controls = enumerator.EnumerateChildControls(handle);
        }
        catch
        {
            // Unknown, not empty. The caller is told the difference.
            return new DialogContent(false, null, Array.Empty<DialogButtonInfo>());
        }

        var labels = controls
            .Where(control => IsClass(control, "Static"))
            .Select(control => control.Text?.Trim())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        var buttons = controls
            .Where(control => IsClass(control, "Button"))
            .Where(control => !string.IsNullOrWhiteSpace(control.Text))
            .Select(control => new DialogButtonInfo
            {
                Text = control.Text!.Trim(),
                ControlId = control.ControlId,
                IsEnabled = control.IsEnabled
            })
            .ToArray();

        var text = labels.Length == 0 ? null : string.Join(" ", labels);
        return new DialogContent(true, text, buttons);
    }

    private static bool IsClass(ControlSnapshot control, string expected) =>
        control.ClassName is not null &&
        control.ClassName.Contains(expected, StringComparison.OrdinalIgnoreCase);

    private sealed record DialogContent(bool Read, string? Text, IReadOnlyList<DialogButtonInfo> Buttons);

    /// <summary>
    /// Presses exactly one button on exactly one dialog, and only when a person
    /// already decided which. Every other path returns without touching the UI.
    ///
    /// The order of the checks matters: the dialog is located and disambiguated
    /// before any decision is consulted, so a request naming the wrong dialog
    /// fails on the dialog rather than quietly resolving against another one.
    /// </summary>
    public DialogDismissResponse Dismiss(
        DialogDismissRequest request,
        IWindowEnumerator enumerator,
        IDialogActuator actuator,
        DialogDecisionStore decisions)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(enumerator);
        ArgumentNullException.ThrowIfNull(actuator);
        ArgumentNullException.ThrowIfNull(decisions);

        if (string.IsNullOrWhiteSpace(request.ExpectedTitle))
        {
            throw new DialogToolException(
                ErrorCode.InvalidRequest,
                "dismiss_dialog requires expected_title: the caller has to state which dialog they mean.");
        }

        var title = request.ExpectedTitle.Trim();
        var classification = Classify(enumerator.EnumerateTopLevelWindows());
        var matches = classification.Dialogs
            .Where(window => string.Equals(window.Title, title, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length == 0)
        {
            throw new DialogToolException(ErrorCode.DialogNotFound, $"No dialog titled '{title}' is open in the host.");
        }

        if (matches.Length > 1)
        {
            throw new DialogToolException(
                ErrorCode.DialogAmbiguous,
                $"{matches.Length} dialogs are titled '{title}'; refusing to guess which one to close.");
        }

        var window = matches[0];
        var content = ReadContent(enumerator, window.Handle);

        // Recording happens after the dialog is known to exist and before the
        // resolution, so "decide and apply" is one step for the operator without
        // storing a decision for a dialog that was never there.
        if (!string.IsNullOrWhiteSpace(request.ButtonText) && request.Scope is { } scope)
        {
            decisions.Record(new DialogDecision
            {
                DialogTitle = title,
                ButtonText = request.ButtonText!.Trim(),
                Scope = scope,
                Note = request.Note
            });
        }

        var resolution = decisions.Resolve(title, content.Read ? content.Buttons : null);
        if (!resolution.Resolved)
        {
            return new DialogDismissResponse
            {
                InstanceId = request.InstanceId,
                DialogTitle = title,
                Pressed = false,
                Outcome = resolution.Outcome,
                Options = resolution.Options,
                Message = DescribeRefusal(resolution.Outcome, title)
            };
        }

        var decision = resolution.Decision!;
        var control = enumerator.EnumerateChildControls(window.Handle)
            .FirstOrDefault(candidate =>
                IsClass(candidate, "Button") &&
                string.Equals(candidate.Text?.Trim(), decision.ButtonText, StringComparison.Ordinal));

        if (control is null)
        {
            // The dialog changed between reading and pressing.
            return new DialogDismissResponse
            {
                InstanceId = request.InstanceId,
                DialogTitle = title,
                Pressed = false,
                Outcome = DialogDecisionOutcome.ButtonNotPresent,
                Options = content.Buttons,
                Message = DescribeRefusal(DialogDecisionOutcome.ButtonNotPresent, title)
            };
        }

        if (!actuator.PressButton(window.Handle, control.Handle, control.ControlId))
        {
            throw new DialogToolException(
                ErrorCode.DialogDismissFailed,
                $"The host did not accept the click on '{decision.ButtonText}' in '{title}'.");
        }

        // A posted click is only queued. Success is the dialog being gone, not
        // the message having been sent, or the operator would be told the host
        // is unblocked while it is still waiting on the same modal.
        if (!actuator.WaitUntilClosed(window.Handle, TimeSpan.FromSeconds(5)))
        {
            throw new DialogToolException(
                ErrorCode.DialogDismissFailed,
                $"The click on '{decision.ButtonText}' was delivered but '{title}' is still open.");
        }

        decisions.Consume(decision);

        return new DialogDismissResponse
        {
            InstanceId = request.InstanceId,
            DialogTitle = title,
            Pressed = true,
            Outcome = DialogDecisionOutcome.Resolved,
            ButtonText = decision.ButtonText,
            Scope = decision.Scope,
            Options = content.Buttons
        };
    }

    private static string DescribeRefusal(DialogDecisionOutcome outcome, string title) => outcome switch
    {
        DialogDecisionOutcome.DecisionRequired =>
            $"No decision applies to '{title}'. Choose one of the options and say whether it is the default or a one-time answer.",
        DialogDecisionOutcome.ButtonNotPresent =>
            $"The decided button is not present or not enabled on '{title}'. Nothing was pressed.",
        DialogDecisionOutcome.ContentUnreadable =>
            $"The controls of '{title}' could not be read, so its buttons are unknown rather than absent. Nothing was pressed.",
        _ => null!
    };

    public DialogStatus GetStatus(IWindowEnumerator enumerator)
    {
        ArgumentNullException.ThrowIfNull(enumerator);

        IReadOnlyList<WindowSnapshot> windows;
        try
        {
            windows = enumerator.EnumerateTopLevelWindows();
        }
        catch (Exception ex)
        {
            return new DialogStatus
            {
                Supported = true,
                State = "unavailable",
                HasBlockingDialog = false,
                DialogCount = 0,
                Note = $"Host window enumeration failed: {ex.Message}"
            };
        }

        var classification = Classify(windows);
        return new DialogStatus
        {
            Supported = true,
            State = "active",
            HasBlockingDialog = classification.BlockedByModal,
            DialogCount = classification.Dialogs.Count,
            BlockingDialogTitle = classification.BlockingDialog?.Title,
            Note = classification.MainWindow is null
                ? "No visible unowned host window was found."
                : null
        };
    }

    private static Classification Classify(IReadOnlyList<WindowSnapshot> windows)
    {
        var visible = windows
            .Where(window => window.IsVisible && !string.IsNullOrWhiteSpace(window.Title))
            .ToArray();

        var mainWindow = visible
            .Where(window => window.OwnerHandle == 0)
            .OrderByDescending(window => window.Area)
            .FirstOrDefault();

        var dialogs = visible
            .Where(window => window.OwnerHandle != 0)
            .Where(window => mainWindow is null || window.Handle != mainWindow.Handle)
            .OrderByDescending(window => window.Area)
            .ToArray();

        var blockedByModal = mainWindow is not null &&
            !mainWindow.IsEnabled &&
            dialogs.Any(dialog => dialog.IsEnabled);

        var blockingDialog = blockedByModal
            ? dialogs.FirstOrDefault(dialog => dialog.IsEnabled)
            : null;

        return new Classification(mainWindow, dialogs, blockedByModal, blockingDialog);
    }

    private static int SafeProcessId(IWindowEnumerator enumerator)
    {
        try
        {
            return enumerator.ProcessId;
        }
        catch
        {
            return 0;
        }
    }

    private static string FormatHandle(long handle) =>
        "0x" + handle.ToString("X16", CultureInfo.InvariantCulture);

    private sealed record Classification(
        WindowSnapshot? MainWindow,
        IReadOnlyList<WindowSnapshot> Dialogs,
        bool BlockedByModal,
        WindowSnapshot? BlockingDialog);
}

/// <summary>
/// Adapts <see cref="DialogMonitorService"/> to the diagnostics interface used by
/// <c>civil3d_status</c>.
/// </summary>
public sealed class WindowDialogMonitor : IDialogMonitor
{
    private readonly IWindowEnumerator _enumerator;
    private readonly DialogMonitorService _service = new();
    private readonly DialogDecisionStore _decisions;
    private readonly string? _storePath;

    public WindowDialogMonitor(IWindowEnumerator enumerator)
        : this(enumerator, FileDialogDecisionRepository.Default())
    {
    }

    public WindowDialogMonitor(IWindowEnumerator enumerator, IDialogDecisionRepository decisions)
    {
        _enumerator = enumerator;
        _decisions = new DialogDecisionStore(decisions);
        _storePath = (decisions as FileDialogDecisionRepository)?.Path;
    }

    public DialogStatus GetStatus() => _service.GetStatus(_enumerator);

    public DialogListResponse List(string instanceId) => _service.List(instanceId, _enumerator);

    /// <summary>
    /// The decision list: what the operator has already decided, plus any dialog
    /// on screen that still needs an answer, with the options to choose from.
    /// </summary>
    public DialogDecisionListResponse ListDecisions(string instanceId)
    {
        var stored = _decisions.List();
        var pending = new List<DialogPendingDecision>();

        foreach (var dialog in _service.List(instanceId, _enumerator).Dialogs)
        {
            // Only windows actually blocking the host need a decision. A docked
            // palette is an owned window too, and listing it would bury the real
            // decisions under noise the operator can do nothing about.
            if (string.IsNullOrWhiteSpace(dialog.Title) || !dialog.IsModalCandidate)
            {
                continue;
            }

            var resolution = _decisions.Resolve(dialog.Title, dialog.ContentRead ? dialog.Buttons : null);
            if (resolution.Resolved)
            {
                continue;
            }

            pending.Add(new DialogPendingDecision
            {
                DialogTitle = dialog.Title,
                Text = dialog.Text,
                Outcome = resolution.Outcome,
                Options = resolution.Options
            });
        }

        return new DialogDecisionListResponse
        {
            InstanceId = instanceId,
            Count = stored.Count,
            Decisions = stored,
            Pending = pending,
            StorePath = _storePath
        };
    }

    /// <summary>
    /// Presses the decided button on one dialog. Refuses, without touching the
    /// UI, whenever a person has not already decided.
    /// </summary>
    public DialogDismissResponse Dismiss(DialogDismissRequest request, IDialogActuator actuator) =>
        _service.Dismiss(request, _enumerator, actuator, _decisions);

    public DialogDecisionSetResponse SetDecision(DialogDecisionSetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var decision = _decisions.Record(new DialogDecision
        {
            DialogTitle = request.DialogTitle,
            ButtonText = request.ButtonText,
            Scope = request.Scope,
            Note = request.Note
        });

        return new DialogDecisionSetResponse
        {
            InstanceId = request.InstanceId,
            Decision = decision,
            Persisted = decision.Scope == DialogDecisionScope.Default,
            Count = _decisions.List().Count
        };
    }
}
