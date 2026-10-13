using System.Runtime.InteropServices;
using System.Text;

namespace Civil3D.HybridMcp.Diagnostics;

/// <summary>
/// Enumerates the current process's top-level windows through Win32.
///
/// Every call here is deliberately non-blocking. Window titles are read with
/// SendMessageTimeout and SMTO_ABORTIFHUNG rather than GetWindowText, because
/// GetWindowText sends WM_GETTEXT synchronously to same-process windows and
/// would hang the caller exactly when the host thread is stuck, which is the
/// case this monitor exists to diagnose.
/// </summary>
public sealed class Win32WindowEnumerator : IWindowEnumerator
{
    private const uint WM_GETTEXT = 0x000D;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint GW_OWNER = 4;
    private const int TitleTimeoutMs = 250;
    private const int MaxTitleLength = 512;

    /// <summary>Upper bound on the child sweep of a single dialog.</summary>
    private const int MaxChildControls = 128;

    public int ProcessId { get; } = Environment.ProcessId;

    public IReadOnlyList<WindowSnapshot> EnumerateTopLevelWindows()
    {
        var processId = ProcessId;
        var windows = new List<WindowSnapshot>();

        var callback = new EnumWindowsProc((handle, _) =>
        {
            try
            {
                if (GetWindowThreadProcessId(handle, out var owningProcessId) == 0 || owningProcessId != processId)
                {
                    return true;
                }

                windows.Add(new WindowSnapshot
                {
                    Handle = handle.ToInt64(),
                    Title = ReadTitle(handle),
                    ClassName = ReadClassName(handle),
                    IsVisible = IsWindowVisible(handle),
                    IsEnabled = IsWindowEnabled(handle),
                    OwnerHandle = GetWindow(handle, GW_OWNER).ToInt64(),
                    Area = ReadArea(handle)
                });
            }
            catch
            {
                // A window can disappear between enumeration and inspection.
                // Skipping it is correct; failing the whole sweep is not.
            }

            return true;
        });

        if (!EnumWindows(callback, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();

            // EnumWindows returns false when a callback stops enumeration early.
            // Treat a zero error code as a benign stop, not a failure.
            if (error != 0 && windows.Count == 0)
            {
                throw new InvalidOperationException($"EnumWindows failed with Win32 error {error}.");
            }
        }

        GC.KeepAlive(callback);
        return windows;
    }

    /// <summary>
    /// Enumerates one window's child controls.
    ///
    /// Bounded on purpose: this runs while the host may be hung, so the sweep
    /// stops at <see cref="MaxChildControls"/> and every caption still goes
    /// through SendMessageTimeout with SMTO_ABORTIFHUNG.
    /// </summary>
    public IReadOnlyList<ControlSnapshot> EnumerateChildControls(long windowHandle)
    {
        var controls = new List<ControlSnapshot>();
        var parent = new IntPtr(windowHandle);

        var callback = new EnumWindowsProc((handle, _) =>
        {
            try
            {
                controls.Add(new ControlSnapshot
                {
                    Handle = handle.ToInt64(),
                    ControlId = GetDlgCtrlID(handle),
                    ClassName = ReadClassName(handle),
                    Text = ReadTitle(handle),
                    IsVisible = IsWindowVisible(handle),
                    IsEnabled = IsWindowEnabled(handle)
                });
            }
            catch
            {
                // A control can disappear mid-sweep; skipping it beats failing.
            }

            return controls.Count < MaxChildControls;
        });

        if (!EnumChildWindows(parent, callback, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 0 && controls.Count == 0)
            {
                throw new InvalidOperationException($"EnumChildWindows failed with Win32 error {error}.");
            }
        }

        GC.KeepAlive(callback);
        return controls;
    }

    private static string? ReadTitle(IntPtr handle)
    {
        var buffer = new StringBuilder(MaxTitleLength);
        var result = SendMessageTimeout(
            handle,
            WM_GETTEXT,
            (IntPtr)MaxTitleLength,
            buffer,
            SMTO_ABORTIFHUNG,
            TitleTimeoutMs,
            out _);

        if (result == IntPtr.Zero)
        {
            return null;
        }

        var title = buffer.ToString();
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static string? ReadClassName(IntPtr handle)
    {
        var buffer = new StringBuilder(256);
        var length = GetClassName(handle, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString() : null;
    }

    private static long ReadArea(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var rect))
        {
            return 0;
        }

        var width = (long)rect.Right - rect.Left;
        var height = (long)rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        return width * height;
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr handle, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr handle, StringBuilder buffer, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr handle,
        uint message,
        IntPtr wParam,
        StringBuilder lParam,
        uint flags,
        int timeoutMs,
        out IntPtr result);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

/// <summary>
/// Clicks a dialog button through Win32.
///
/// Uses PostMessage rather than SendMessage on purpose. This runs while the
/// host's main thread is blocked by the modal; a synchronous send could park the
/// caller behind that same block. The modal runs its own message pump, so the
/// posted WM_COMMAND is processed by the window that is actually alive.
///
/// The click is addressed by control id and button handle, both read from the
/// live dialog immediately beforehand. Nothing here searches for a button, picks
/// a default, or presses by position.
/// </summary>
public sealed class Win32DialogActuator : IDialogActuator
{
    private const uint BM_CLICK = 0x00F5;

    /// <summary>
    /// Clicks by posting BM_CLICK to the button itself.
    ///
    /// The obvious alternative, posting WM_COMMAND to the dialog with the
    /// control id, does not work here. Live validation against the Civil 3D
    /// `References - Not Found Files` dialog showed both of its buttons report a
    /// control id of 0, so a WM_COMMAND addressed by id cannot tell them apart.
    /// BM_CLICK goes to one specific window handle, so it stays unambiguous no
    /// matter how the dialog numbers its controls.
    /// </summary>
    public bool PressButton(long dialogHandle, long buttonHandle, int controlId)
    {
        try
        {
            return PostMessage(new IntPtr(buttonHandle), BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Polls until the dialog window stops existing. Uses IsWindow, which does
    /// not send a message, so it cannot block on the very UI thread the modal is
    /// holding.
    /// </summary>
    public bool WaitUntilClosed(long dialogHandle, TimeSpan timeout)
    {
        var handle = new IntPtr(dialogHandle);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (!IsWindow(handle))
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return !IsWindow(handle);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr handle);
}
