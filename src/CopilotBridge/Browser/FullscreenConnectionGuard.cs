using System.Runtime.InteropServices;
using CopilotBridge.Core;

namespace CopilotBridge.Browser;

internal sealed class FullscreenProtectionException : InvalidOperationException
{
    internal FullscreenProtectionException()
        : base("A foreground full-screen window is active; new Edge connections are paused.")
    {
    }
}

internal readonly record struct ScreenBounds(int Left, int Top, int Right, int Bottom)
{
    internal int Width => Right - Left;
    internal int Height => Bottom - Top;
}

internal readonly record struct ForegroundWindowSnapshot(
    ScreenBounds Window,
    ScreenBounds Monitor);

internal static class FullscreenConnectionGuard
{
    internal const int EdgeTolerancePixels = 8;
    private const uint MonitorDefaultToNearest = 2;

    internal static void ThrowIfBlocked(
        BridgeSettings settings,
        bool bypassProtection = false,
        Func<ForegroundWindowSnapshot?>? snapshotProvider = null)
    {
        if (!settings.FullscreenProtectionEnabled || bypassProtection) return;

        var snapshot = (snapshotProvider ?? TryReadSnapshot)();
        if (snapshot is null || !CoversMonitor(snapshot.Value.Window, snapshot.Value.Monitor)) return;

        DiagnosticLog.WriteInfo(
            "fullscreen_connection_blocked",
            $"window={Format(snapshot.Value.Window)} monitor={Format(snapshot.Value.Monitor)}");
        throw new FullscreenProtectionException();
    }

    internal static bool CoversMonitor(
        ScreenBounds window,
        ScreenBounds monitor,
        int tolerance = EdgeTolerancePixels) =>
        tolerance >= 0 &&
        window.Width > 0 &&
        window.Height > 0 &&
        monitor.Width > 0 &&
        monitor.Height > 0 &&
        window.Left <= monitor.Left + tolerance &&
        window.Top <= monitor.Top + tolerance &&
        window.Right >= monitor.Right - tolerance &&
        window.Bottom >= monitor.Bottom - tolerance;

    private static ForegroundWindowSnapshot? TryReadSnapshot()
    {
        var window = GetForegroundWindow();
        if (window == 0 || IsIconic(window) || !GetWindowRect(window, out var windowRect)) return null;

        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref monitorInfo)) return null;

        return new ForegroundWindowSnapshot(ToBounds(windowRect), ToBounds(monitorInfo.Monitor));
    }

    private static ScreenBounds ToBounds(NativeRect value) =>
        new(value.Left, value.Top, value.Right, value.Bottom);

    private static string Format(ScreenBounds value) =>
        $"{value.Left},{value.Top},{value.Right},{value.Bottom}";

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        internal int Size;
        internal NativeRect Monitor;
        internal NativeRect WorkArea;
        internal uint Flags;
    }
}
