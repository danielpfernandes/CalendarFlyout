using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CalendarFlyout.Native;

internal static class WindowEffects
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    public static void Initialize(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        // TOOLWINDOW exclui também do Alt+Tab; APPWINDOW é removido.
        SetWindowLong(hwnd, -20, (GetWindowLong(hwnd, -20) | 0x80) & ~0x40000);
        SetAttribute(hwnd, 33, 2); // DWMWA_WINDOW_CORNER_PREFERENCE = ROUND
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
            target.BackgroundColor = Colors.Transparent;
    }

    public static bool ApplyTheme(Window window, bool dark, bool highContrast)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;
        SetAttribute(hwnd, 20, dark ? 1 : 0); // DWMWA_USE_IMMERSIVE_DARK_MODE
        // Acrylic oficial exige Windows 11 22H2. Builds anteriores recebem fundo sólido.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return false;
        return SetAttribute(hwnd, 38, highContrast ? 1 : 3) >= 0 && !highContrast;
    }

    private static int SetAttribute(IntPtr hwnd, int attribute, int value) =>
        DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));

    public static void Place(Window window, bool activate)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        GetCursorPos(out var point);
        var monitor = MonitorFromPoint(point, 2); // Monitor sob o ícone clicado, inclusive telas secundárias.
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;
        // rcWork já desconta a barra de tarefas. Coordenadas Win32 são pixels físicos.
        SetWindowPos(hwnd, IntPtr.Zero, info.Work.Left, info.Work.Top, 0, 0, 0x15);
        var scale = Math.Max(96, GetDpiForWindow(hwnd)) / 96.0;
        var gap = (int)Math.Round(12 * scale);
        var width = Math.Min((int)Math.Round(392 * scale), info.Work.Right - info.Work.Left - 2 * gap);
        var height = Math.Min((int)Math.Round(620 * scale), info.Work.Bottom - info.Work.Top - 2 * gap);
        SetWindowPos(hwnd, new IntPtr(-1), info.Work.Right - width - gap, info.Work.Bottom - height - gap,
            Math.Max(1, width), Math.Max(1, height), 0x10);
        if (activate) { SetForegroundWindow(hwnd); window.Activate(); }
    }
}
