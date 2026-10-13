using Civil3D.HybridMcp.Protocol;

namespace Civil3D.HybridMcp.Diagnostics;

public interface IBridgeHealthProvider
{
    BridgeHealth GetHealth();
}

public interface IActiveCommandProvider
{
    string? GetActiveCommand();
}

public interface IDialogMonitor
{
    DialogStatus GetStatus();
}

public sealed class PartialDialogMonitor : IDialogMonitor
{
    public DialogStatus GetStatus() =>
        new()
        {
            Supported = false,
            State = "partial",
            HasBlockingDialog = false,
            DialogCount = 0,
            Note = "Dialog monitor foundation is present; Win32 modal enumeration is not implemented in Phase 1."
        };
}

