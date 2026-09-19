using System.Runtime.InteropServices;

namespace xyz.GrpcHost;

/// <summary>
/// 托盘灯用到的 Win32：隐藏消息窗口、Shell_NotifyIcon、弹出菜单、拿像素拼图标。
/// </summary>
internal sealed partial class HostTrayIcon
{
    private const string WindowClassName = "xyz.GrpcHost.Tray";
    private const int IconId = 1;
    private const nuint PromoteTimerId = 1;

    // 窗口消息；WM_APP（0x8000）往后是自定义的。
    private const uint WmNull = 0x0000;
    private const uint WmDestroy = 0x0002;
    private const uint WmClose = 0x0010;
    private const uint WmTimer = 0x0113;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmTray = 0x8001;     // 托盘图标回调
    private const uint WmRefresh = 0x8002;  // 状态变了，换灯
    private const uint WmBalloon = 0x8003;  // 冒气泡说当前状态
    private const uint WmTeardown = 0x8004; // Dispose：摘灯、退消息循环

    // Shell_NotifyIcon
    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NifMessage = 0x01;
    private const uint NifIcon = 0x02;
    private const uint NifTip = 0x04;
    private const uint NifInfo = 0x10;
    private const uint NiifInfo = 0x01;
    private const uint NiifError = 0x03;

    // 弹出菜单
    private const uint MfString = 0x0000;
    private const uint MfGrayed = 0x0001;
    private const uint MfSeparator = 0x0800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmRightAlign = 0x0008;
    private const uint TpmBottomAlign = 0x0020;
    private const uint TpmNoNotify = 0x0080;
    private const uint TpmReturnCmd = 0x0100;
    private const int CommandOpenLog = 1;
    private const int CommandExit = 2;

    // 消息框
    private const uint MbYesNo = 0x0004;
    private const uint MbIconWarning = 0x0030;
    private const uint MbDefButton2 = 0x0100;
    private const uint MbSetForeground = 0x0001_0000;
    private const uint MbTopMost = 0x0004_0000;
    private const int IdYes = 6;

    private const int SmCxSmIcon = 49;
    private const uint MsgFltAllow = 1;
    private const uint DibRgbColors = 0;

    /// <summary>
    /// DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2。
    /// </summary>
    private static readonly IntPtr DpiAwarenessContextPerMonitorAwareV2 = new(-4);

    private delegate IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public Point pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out Msg message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref Msg message);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll")]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr window, uint message, uint action, IntPtr filterStatus);

    [DllImport("user32.dll")]
    private static extern nuint SetTimer(IntPtr window, nuint id, uint elapse, IntPtr timerProc);

    [DllImport("user32.dll")]
    private static extern bool KillTimer(IntPtr window, nuint id);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref IconInfo info);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader header, uint usage,
        out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, byte[] bits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
}
