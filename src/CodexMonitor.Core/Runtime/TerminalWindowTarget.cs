using System.Runtime.InteropServices;

namespace CodexMonitor.Core.Runtime;

public sealed record TerminalWindowTarget(long WindowHandle, uint ProcessId)
{
    private const int SwRestore = 9;
    private const uint FlashwTray = 0x00000002;
    private const uint FlashwTimerNoForeground = 0x0000000C;

    public static TerminalWindowTarget? CaptureForeground()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return null;
        }

        GetWindowThreadProcessId(window, out var processId);
        return processId == 0
            ? null
            : new TerminalWindowTarget(window.ToInt64(), processId);
    }

    public bool TryActivate()
    {
        if (!OperatingSystem.IsWindows() || WindowHandle == 0 || ProcessId == 0)
        {
            return false;
        }

        var window = new IntPtr(WindowHandle);
        if (!IsWindow(window))
        {
            return false;
        }

        GetWindowThreadProcessId(window, out var currentProcessId);
        if (currentProcessId != ProcessId)
        {
            return false;
        }

        if (IsIconic(window))
        {
            ShowWindowAsync(window, SwRestore);
        }

        if (SetForegroundWindow(window))
        {
            return true;
        }

        var flashInfo = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            Window = window,
            Flags = FlashwTray | FlashwTimerNoForeground,
            Count = 3,
            Timeout = 0
        };
        FlashWindowEx(ref flashInfo);
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo flashInfo);
}
