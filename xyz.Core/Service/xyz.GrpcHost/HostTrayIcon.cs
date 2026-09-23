using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using xyz.Common.Log;

namespace xyz.GrpcHost;

/// <summary>
/// 后端托盘状态灯：宿主是 WinExe，不弹控制台黑框，看右下角这颗灯（跟客户端四色灯同色）——
/// 灰 = 启动中 / 正在退出，绿 = 运行中（gRPC 端口已在监听），红 = 启动失败（悬停或气泡看原因，完整异常在日志里）。
/// 左键、右键都弹菜单：当前状态、打开日志目录、退出后端。
/// 直接调 Win32（Shell_NotifyIcon），不引 WinForms：引了宿主得改 net10.0-windows，输出目录跟着从 bin\…\net10.0 换走，
/// 里面的 Config、Modules、数据库都得搬。
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class HostTrayIcon : IDisposable
{
    private const string Title = "xyz 后端";

    /// <summary>
    /// 本机唯一后端的锁；Global 跨会话，远程桌面里再开一个也算重复。
    /// </summary>
    private const string InstanceMutexName = @"Global\xyz.GrpcHost";

    /// <summary>
    /// 重复启动时捅一下已在跑的那个，让它的灯冒气泡。
    /// </summary>
    private const string PokeEventName = @"Global\xyz.GrpcHost.Poke";

    // 灯色跟客户端 DarkColors.xaml 走：DarkRunningStatus / DarkAlarmStatus；灰取 Material Grey 500。
    private const int GrayLamp = 0x9E9E9E;
    private const int GreenLamp = 0x43A047;
    private const int RedLamp = 0xEF5350;

    /// <summary>
    /// Explorer 挂上图标后才建 Win11 的托盘记录，按秒再找几次（见 <see cref="TryPromote"/>）。
    /// </summary>
    private const int PromoteMaxTries = 10;

    private enum HostState
    {
        Starting,
        Running,
        Failed,
        Stopping,
    }

    private readonly object _gate = new();
    private readonly CancellationTokenSource _exit = new();
    private readonly ManualResetEventSlim _created = new();

    /// <summary>
    /// 窗口过程委托得一直攥着：被 GC 回收了，原生那边再回调就飞了。
    /// </summary>
    private readonly WndProc _wndProc;

    private readonly Thread _thread;
    private readonly EventWaitHandle? _poke;
    private readonly RegisteredWaitHandle? _pokeWait;

    private HostState _state = HostState.Starting;
    private string _detail = string.Empty;

    // 以下只在托盘线程上读写（_window 建好后只读）。
    private IntPtr _window;
    private IntPtr _icon;
    private int _iconColor;
    private int _iconSize;
    private uint _taskbarCreated;
    private int _promoteTries;

    /// <summary>
    /// 起托盘线程、挂上灰灯（启动中）。没有桌面（比如当服务跑）挂不上也不影响后端，只记一条警告。
    /// </summary>
    public HostTrayIcon()
    {
        _wndProc = OnMessage;
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "HostTray" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _created.Wait();

        if (_window == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _poke = new EventWaitHandle(false, EventResetMode.AutoReset, PokeEventName);
            _pokeWait = ThreadPool.RegisterWaitForSingleObject(
                _poke, (_, _) => Post(WmBalloon), null, Timeout.Infinite, executeOnlyOnce: false);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            LogHelper.Warn("Host", $"重复启动提醒挂不上，不影响运行：{exception.Message}");
        }
    }

    /// <summary>
    /// 托盘上点了"退出后端"（或外面发来关窗，如 taskkill 不带 /F）。
    /// </summary>
    public CancellationToken ExitRequested => _exit.Token;

    /// <summary>
    /// 占"本机唯一后端"的名额。已经有一个在跑（灯绿灯红都算）就捅它一下、让它的灯冒气泡，返回 null——
    /// 调用方直接退，别再去连一遍设备。
    /// </summary>
    public static Mutex? ClaimInstance()
    {
        try
        {
            var mutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
            if (createdNew)
            {
                return mutex;
            }

            mutex.Dispose();
        }
        catch (UnauthorizedAccessException)
        {
            // 在跑的那个是管理员身份起的，这边打不开它的锁：一样当已在运行。
        }

        try
        {
            using var poke = EventWaitHandle.OpenExisting(PokeEventName);
            poke.Set();
        }
        catch (Exception exception) when (
            exception is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
        {
            // 捅不到就算了（它可能正在退出），反正这个不能再起。
        }

        return null;
    }

    /// <summary>
    /// gRPC 端口监听上了：灯变绿。
    /// </summary>
    public void SetRunning(string address)
    {
        SetState(HostState.Running, address);
    }

    /// <summary>
    /// 启动失败：灯变红，冒气泡说原因。
    /// </summary>
    public void SetFailed(string reason)
    {
        if (SetState(HostState.Failed, reason))
        {
            Post(WmBalloon);
        }
    }

    /// <summary>
    /// 启动失败后留着红灯，等人在托盘上点退出；托盘没挂上就不等，直接返回。
    /// </summary>
    public void WaitForExit()
    {
        if (_window != IntPtr.Zero)
        {
            _exit.Token.WaitHandle.WaitOne();
        }
    }

    public void Dispose()
    {
        _pokeWait?.Unregister(null);
        _poke?.Dispose();

        if (_window != IntPtr.Zero)
        {
            PostMessageW(_window, WmTeardown, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _exit.Dispose();
        _created.Dispose();
    }

    private bool SetState(HostState state, string detail)
    {
        lock (_gate)
        {
            // 点了退出就一直灰着，别被随后才到的"启动完成"刷回绿灯。
            if (_state == HostState.Stopping)
            {
                return false;
            }

            _state = state;
            _detail = detail;
        }

        Post(WmRefresh);
        return true;
    }

    private (HostState State, string Detail) Snapshot()
    {
        lock (_gate)
        {
            return (_state, _detail);
        }
    }

    private void Post(uint message)
    {
        if (_window != IntPtr.Zero)
        {
            PostMessageW(_window, message, IntPtr.Zero, IntPtr.Zero);
        }
    }

    #region 托盘线程：消息窗口与消息循环

    private void MessageLoop()
    {
        bool created;
        try
        {
            created = CreateWindow();
        }
        catch (Exception exception)
        {
            LogHelper.Warn("Host", $"托盘状态灯挂不上，后端照常运行：{exception.Message}");
            created = false;
        }
        finally
        {
            _created.Set();
        }

        if (!created)
        {
            return;
        }

        while (GetMessageW(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    private bool CreateWindow()
    {
        // 按屏幕真实 DPI 画灯：不声明的话系统按 96 DPI 只要 16px，高分屏托盘上被放大发糊。只管这条线程建的窗口。
        SetThreadDpiAwarenessContext(DpiAwarenessContextPerMonitorAwareV2);

        var instance = GetModuleHandleW(null);
        var windowClass = new WndClassEx
        {
            cbSize = Marshal.SizeOf<WndClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = WindowClassName,
        };
        RegisterClassExW(ref windowClass);

        // 隐藏的普通顶层窗口；不用纯消息窗口（HWND_MESSAGE），那种收不到 Explorer 重启时广播的 TaskbarCreated。
        _window = CreateWindowExW(0, WindowClassName, Title, 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (_window == IntPtr.Zero)
        {
            LogHelper.Warn("Host", $"托盘状态灯挂不上，后端照常运行：{new Win32Exception().Message}");
            return false;
        }

        // 后端以管理员身份跑时，普通权限的 Explorer 广播来的 TaskbarCreated 会被 UIPI 拦掉，放行。
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        ChangeWindowMessageFilterEx(_window, _taskbarCreated, MsgFltAllow, IntPtr.Zero);

        Apply();
        SetTimer(_window, PromoteTimerId, 1000, IntPtr.Zero);
        return true;
    }

    private IntPtr OnMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            switch (message)
            {
                case WmTray:
                    // 老版回调：lParam 就是鼠标消息。
                    if ((uint)lParam is WmLButtonUp or WmRButtonUp)
                    {
                        ShowMenu();
                    }

                    return IntPtr.Zero;

                case WmRefresh:
                    Apply();
                    return IntPtr.Zero;

                case WmBalloon:
                    ShowBalloon();
                    return IntPtr.Zero;

                case WmTimer:
                    if (TryPromote() || ++_promoteTries >= PromoteMaxTries)
                    {
                        KillTimer(window, PromoteTimerId);
                    }

                    return IntPtr.Zero;

                case WmClose:
                    // 外面发来的关窗（taskkill 不带 /F 就是这么关的）：当退出处理，不弹确认。
                    RequestExit(confirm: false);
                    return IntPtr.Zero;

                case WmTeardown:
                    DestroyWindow(window);
                    return IntPtr.Zero;

                case WmDestroy:
                    RemoveIcon();
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }

            if (message == _taskbarCreated && message != 0)
            {
                // Explorer 重启过，托盘上的图标全没了，重新挂。
                Apply();
                return IntPtr.Zero;
            }
        }
        catch (Exception exception)
        {
            // 异常不能穿回原生回调，穿过去会直接带崩整个后端。
            LogHelper.Warn("Host", $"托盘状态灯出错：{exception.Message}");
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    #endregion

    #region 灯、提示、气泡

    /// <summary>
    /// 按当前状态换灯和悬停提示；没挂上（第一次，或 Explorer 重启过）就挂上。
    /// </summary>
    private void Apply()
    {
        var (state, detail) = Snapshot();
        var size = GetSystemMetricsForDpi(SmCxSmIcon, GetDpiForWindow(_window));
        if (size <= 0)
        {
            size = 16;
        }

        var color = state switch
        {
            HostState.Running => GreenLamp,
            HostState.Failed => RedLamp,
            _ => GrayLamp,
        };

        var stale = IntPtr.Zero;
        if (_icon == IntPtr.Zero || size != _iconSize || color != _iconColor)
        {
            stale = _icon;
            _icon = CreateLampIcon(size, color);
            _iconSize = size;
            _iconColor = color;
        }

        var tooltip = Clip(TooltipOf(state, detail), 127);
        var data = NewData(NifMessage | NifIcon | NifTip);
        data.hIcon = _icon;
        data.szTip = tooltip;
        if (!Shell_NotifyIconW(NimModify, ref data))
        {
            // 还没挂上（第一次，或 Explorer 重启过）：先用程序名挂——Win11 拿第一次挂时的提示当这颗图标的名字，
            // 挂着"启动中…"就一直叫"启动中…"了——挂上再换成当前状态。
            data.szTip = Title;
            if (Shell_NotifyIconW(NimAdd, ref data))
            {
                data.szTip = tooltip;
                Shell_NotifyIconW(NimModify, ref data);
            }
        }

        // 托盘换上新灯了，旧的才能销毁。
        if (stale != IntPtr.Zero)
        {
            DestroyIcon(stale);
        }
    }

    private void ShowBalloon()
    {
        var (state, detail) = Snapshot();
        var (title, text, flags) = state switch
        {
            HostState.Running => ($"{Title}已在运行", $"{detail}，右键这颗灯可以打开日志或退出。", NiifInfo),
            HostState.Failed => ($"{Title}启动失败", $"{Clip(detail, 200)}\n右键这颗灯可以打开日志或退出。", NiifError),
            HostState.Stopping => ($"{Title}正在退出", "等灯消失了再启动。", NiifInfo),
            _ => ($"{Title}正在启动", "灯变绿就好了。", NiifInfo),
        };

        var data = NewData(NifInfo);
        data.szInfoTitle = Clip(title, 63);
        data.szInfo = Clip(text, 255);
        data.dwInfoFlags = flags;
        Shell_NotifyIconW(NimModify, ref data);
    }

    private void RemoveIcon()
    {
        var data = NewData(0);
        Shell_NotifyIconW(NimDelete, ref data);

        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    private NotifyIconData NewData(uint flags)
    {
        return new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _window,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = WmTray,
            szTip = string.Empty,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };
    }

    private static string TooltipOf(HostState state, string detail)
    {
        return state switch
        {
            HostState.Running => $"{Title}：运行中\n{detail}",
            HostState.Failed => $"{Title}：启动失败\n{detail}",
            HostState.Stopping => $"{Title}：正在退出…",
            _ => $"{Title}：启动中…",
        };
    }

    private static string Clip(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
    }

    #endregion

    #region 右键菜单

    private void ShowMenu()
    {
        var (state, detail) = Snapshot();
        var status = state switch
        {
            HostState.Running => $"运行中 · {detail}",
            HostState.Failed => $"启动失败：{Clip(detail.ReplaceLineEndings(" "), 40)}",
            HostState.Stopping => "正在退出…",
            _ => "启动中…",
        };

        var menu = CreatePopupMenu();
        AppendMenuW(menu, MfString | MfGrayed, 0, status.Replace("&", "&&"));
        AppendMenuW(menu, MfSeparator, 0, null);
        AppendMenuW(menu, MfString, CommandOpenLog, "打开日志目录");
        AppendMenuW(menu, state == HostState.Stopping ? MfString | MfGrayed : MfString, CommandExit, "退出后端");

        // Win32 老规矩：弹菜单前先把自己的窗口置前、弹完补一条空消息，不然点菜单外面它不收起。
        GetCursorPos(out var point);
        SetForegroundWindow(_window);
        var command = TrackPopupMenuEx(menu,
            TpmRightAlign | TpmBottomAlign | TpmRightButton | TpmNoNotify | TpmReturnCmd,
            point.X, point.Y, _window, IntPtr.Zero);
        PostMessageW(_window, WmNull, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);

        switch (command)
        {
            case CommandOpenLog:
                OpenLogDirectory();
                break;
            case CommandExit:
                RequestExit(confirm: state != HostState.Failed);
                break;
        }
    }

    /// <summary>
    /// 请求退出：灯回灰、宿主开始停。后端在跑（或还在起）时退出会断开客户端、设备也没人管了，人点的先确认。
    /// </summary>
    private void RequestExit(bool confirm)
    {
        if (_exit.IsCancellationRequested)
        {
            return;
        }

        if (confirm
            && MessageBoxW(_window, "退出后客户端会断开连接，设备也不再受控。\n确定退出后端？", Title,
                MbYesNo | MbIconWarning | MbDefButton2 | MbSetForeground | MbTopMost) != IdYes)
        {
            return;
        }

        LogHelper.Info("Host", "托盘请求退出，后端停止中");
        SetState(HostState.Stopping, string.Empty);
        _exit.Cancel();
    }

    private static void OpenLogDirectory()
    {
        try
        {
            var directory = LogFileReader.LogDirectory;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception) when (
            exception is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LogHelper.Warn("Host", $"打不开日志目录：{exception.Message}");
        }
    }

    #endregion

    #region Win11 托盘露出

    /// <summary>
    /// Win11 新出现的托盘图标默认收在"^"里。在 NotifyIconSettings 下找到本 exe 那条，还没人动过（没有 IsPromoted）就置 1，
    /// 让灯直接露在任务栏上；用户在设置里关过（IsPromoted = 0）就不动。Win10 没这个键，什么也不做。
    /// 返回 true 表示不用再找了。
    /// </summary>
    private static bool TryPromote()
    {
        try
        {
            using var settings = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
            var executable = Environment.ProcessPath;
            if (settings is null || executable is null)
            {
                return true;
            }

            var found = false;
            foreach (var name in settings.GetSubKeyNames())
            {
                using var entry = settings.OpenSubKey(name, writable: true);
                if (entry?.GetValue("ExecutablePath") is not string path || !IsThisExecutable(path, executable))
                {
                    continue;
                }

                found = true;
                if (entry.GetValue("IsPromoted") is null)
                {
                    entry.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                }
            }

            return found;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return true;
        }
    }

    /// <summary>
    /// 装在 Program Files 这类已知目录下时，Explorer 把路径前缀记成 {已知目录 GUID}\，比后半截。
    /// </summary>
    private static bool IsThisExecutable(string registered, string executable)
    {
        if (string.Equals(registered, executable, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var end = registered.StartsWith('{') ? registered.IndexOf('}') : -1;
        return end > 0 && executable.EndsWith(registered[(end + 1)..], StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 画灯

    /// <summary>
    /// 画一颗状态灯做托盘图标：本色圆盘，上亮下暗，外圈压暗一道描边（深浅任务栏上都分得清边），左上一点高光。
    /// </summary>
    private static IntPtr CreateLampIcon(int size, int rgb)
    {
        var pixels = RenderLamp(size, rgb);
        var header = new BitmapInfoHeader
        {
            biSize = Marshal.SizeOf<BitmapInfoHeader>(),
            biWidth = size,
            biHeight = -size, // 负数 = 自上而下，跟 pixels 的行序一致
            biPlanes = 1,
            biBitCount = 32,
        };

        var color = CreateDIBSection(IntPtr.Zero, ref header, DibRgbColors, out var bits, IntPtr.Zero, 0);
        if (color == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        Marshal.Copy(pixels, 0, bits, pixels.Length);

        // 掩码全 0：透明度全交给颜色位图的 alpha 通道。单色位图每行按 2 字节对齐。
        var mask = CreateBitmap(size, size, 1, 1, new byte[(size + 15) / 16 * 2 * size]);
        var info = new IconInfo { fIcon = true, hbmMask = mask, hbmColor = color };
        var icon = CreateIconIndirect(ref info);

        DeleteObject(color);
        DeleteObject(mask);
        return icon;
    }

    /// <summary>
    /// 逐像素 4×4 超采样抗锯齿，输出直通（非预乘）ARGB——32 位图标要的就是这种。
    /// </summary>
    private static int[] RenderLamp(int size, int rgb)
    {
        const int samples = 4;
        var pixels = new int[size * size];
        var center = size / 2.0;
        var radius = size * 0.4375; // 16px 时直径 14，四周各留 1px
        var rim = size / 16.0;      // 描边宽 = 16px 图上的 1px，随 DPI 放大
        var red = (rgb >> 16) & 0xFF;
        var green = (rgb >> 8) & 0xFF;
        var blue = rgb & 0xFF;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                double hits = 0, r = 0, g = 0, b = 0;
                for (var sy = 0; sy < samples; sy++)
                {
                    for (var sx = 0; sx < samples; sx++)
                    {
                        var dx = x + (sx + 0.5) / samples - center;
                        var dy = y + (sy + 0.5) / samples - center;
                        var distance = Math.Sqrt(dx * dx + dy * dy);
                        if (distance > radius)
                        {
                            continue;
                        }

                        // 明暗：> 0 往白里提，< 0 往黑里压。
                        double shade;
                        if (distance > radius - rim)
                        {
                            shade = -0.30;
                        }
                        else
                        {
                            shade = 0.18 - 0.30 * (dy + radius) / (2 * radius);
                            var gx = (dx + radius * 0.28) / (radius * 0.46);
                            var gy = (dy + radius * 0.44) / (radius * 0.28);
                            var gloss = 1 - (gx * gx + gy * gy);
                            if (gloss > 0)
                            {
                                shade += 0.6 * gloss;
                            }
                        }

                        r += Shade(red, shade);
                        g += Shade(green, shade);
                        b += Shade(blue, shade);
                        hits++;
                    }
                }

                if (hits == 0)
                {
                    continue;
                }

                var alpha = (int)Math.Round(255 * hits / (samples * samples));
                pixels[y * size + x] = (alpha << 24)
                                       | ((int)Math.Round(r / hits) << 16)
                                       | ((int)Math.Round(g / hits) << 8)
                                       | (int)Math.Round(b / hits);
            }
        }

        return pixels;
    }

    private static double Shade(int channel, double shade)
    {
        return shade >= 0 ? channel + (255 - channel) * Math.Min(shade, 1) : channel * (1 + shade);
    }

    #endregion
}
