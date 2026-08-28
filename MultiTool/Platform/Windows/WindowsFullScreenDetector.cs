using System.Runtime.InteropServices;

namespace MultiTool.Platform.Windows;

internal interface IFullScreenDetector
{
    bool IsForegroundWindowFullScreen();
}

internal sealed class WindowsFullScreenDetector : IFullScreenDetector
{
    public bool IsForegroundWindowFullScreen()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || !GetWindowRect(window, out var windowRect))
        {
            return false;
        }

        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        const int tolerance = 2;
        return windowRect.Left <= monitorInfo.Monitor.Left + tolerance
               && windowRect.Top <= monitorInfo.Monitor.Top + tolerance
               && windowRect.Right >= monitorInfo.Monitor.Right - tolerance
               && windowRect.Bottom >= monitorInfo.Monitor.Bottom - tolerance;
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out Rect rectangle);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }
}
