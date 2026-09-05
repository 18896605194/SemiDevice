using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RejeRobotSimulator.Protocol;
using RejeRobotSimulator.Server;

namespace RejeRobotSimulator;

/// <summary>
/// RejeRobot 机械手仿真器面板 (UserControl, 可被独立窗口或统一仿真器 SimulatorHub 内嵌)。
///
/// 一张 TCP 服务面板：监听上位机连接，按协议解析 @指令; 并返回
/// 「一次发送、两次回复」（先 &gt;; 确认，再 &gt;代码#内容@指令; 结果）。
/// 左侧状态区可实时拨动 使能 / 模式 / Wafer 在位 / 压力 / 订阅；
/// 右侧上为状态快照、下为收发日志。Wafer 订阅开启时，手指片状态变化会自动推送
/// SubWaferEx 事件；主动报错开启时注入错误会按协议主动上报 @Error 帧。
///
/// 生命周期归宿主管 (宿主 = 独立窗口 / SimulatorHub 页签):
///   构造(dataDir) → StartListening() → ... → ShutdownForHost()。
/// 不挂 Loaded/Unloaded —— 页签切换会让面板离开可视树, 服务必须不受影响。
/// </summary>
public partial class RobotPanel : UserControl
{
    /// <summary>心跳帧内容协议未定义，此处用占位格式（如需对接真机请按真机实际帧调整）。</summary>
    private const string HeartbeatFrame = ">00000000#HeartBeat@HeartBeat;";

    private readonly ConfigManager _config;
    private readonly string _dataDir;
    private readonly TcpServer _server;

    private readonly DispatcherTimer _refresh;
    private readonly DispatcherTimer _heartbeat;
    private readonly DispatcherTimer _faultPoll;

    // 定时器回填 UI 时置位，避免触发 CheckedChanged 形成回环
    private bool _suppress;
    // InitializeComponent 之后置位: XAML 解析中 Text="..." 会提前触发 TextChanged,
    // 此时后面的控件字段还没赋值, 事件里必须先看这个标志
    private bool _uiReady;
    // 跑批静默: 每条收发都 BeginInvoke 到 UI 线程写日志框, 压测时这就是把 UI 饿住的主要负载
    private volatile bool _quietLog;

    /// <summary>收发日志集合 (ListBox 绑定)。</summary>
    public ObservableCollection<LogEntry> Logs { get; } = new();

    /// <summary>实例名 (数据目录末段), 宿主用于窗口标题 / 页签标题。</summary>
    public string InstanceName { get; }

    /// <summary>当前/待用监听端口。运行中返回实际端口; 置值写入端口框并同步到服务器。</summary>
    public int ListenPort
    {
        get => _server.IsRunning ? _server.Port : ParseInt(PortText.Text, _server.Port, 1, 65535);
        set
        {
            _server.Port = Math.Clamp(value, 1, 65535);
            PortText.Text = _server.Port.ToString();
        }
    }

    /// <summary>开始监听 (宿主契约; 无人值守批量拉起用)。</summary>
    public void StartListening() => StartServer();

    public RobotPanel(string dataDir)
    {
        _dataDir = dataDir;
        _config = new ConfigManager(dataDir);
        _server = new TcpServer(_config.Config.Port);

        InitializeComponent();
        _uiReady = true;   // XAML 事件处理器从现在起可以安全访问控件字段
        DataContext = this;

        // 崩溃钩子不上移到面板: 同一进程多面板会重复注册重复吞异常, 由宿主 App 统一落盘 crash.log

        InstanceName = Path.GetFileName(dataDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        InstanceTag.Text = "实例: " + InstanceName;

        // 滚动/裁剪必须等生成器处理完本次变更后再做 —— 在 CollectionChanged 里同步
        // 操作 ListBox 会重入 ItemContainerGenerator, 导致"项源不一致"崩溃
        Logs.CollectionChanged += (_, _) =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)(() =>
            {
                if (Logs.Count > 500) Logs.RemoveAt(0);
                if (Logs.Count > 0) LogList.ScrollIntoView(Logs[^1]);
            }));
        };

        // 工具栏从 config.json 回填 (触发 TextChanged 即时写到 _server)
        var cfg = _config.Config;
        PortText.Text = cfg.Port.ToString();
        AckDelayText.Text = cfg.AckDelayMs.ToString();
        RspDelayText.Text = cfg.ResponseDelayMs.ToString();
        MotionDelayText.Text = cfg.MotionDelayMs.ToString();
        FailRateText.Text = cfg.FailureRate.ToString();

        WireServerEvents();

        // 手指在位变化 → 立即推 SubWaferEx (不靠定时器比对快照)
        _server.State.ArmWaferChanged += OnStateArmWaferChanged;

        _refresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _refresh.Tick += (_, _) => RefreshUi();
        _refresh.Start();

        _heartbeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3000) };
        _heartbeat.Tick += (_, _) =>
        {
            if (_server.IsRunning && _server.State.HeartbeatEnabled)
                _server.Broadcast(HeartbeatFrame);
        };

        // 故障注入旗标轮询 (无人值守冒烟用)
        _faultPoll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _faultPoll.Tick += OnFaultFlagPoll;
        _faultPoll.Start();

        RefreshUi();
    }

    // ── 故障注入 ─────────────────────────────────────────────────
    // 数据目录下放旗标文件即生效, 删掉即恢复 (无人值守冒烟用):
    //   fault.robot.error     注入控制器错误码 (等价于点[注入错误]按钮)
    //   fault.robot.noverify  Pick/Place 不更新手指在位, 让上位机取放片后置校验失败
    // 文件都不存在时行为与加这段之前完全一致。

    private void OnFaultFlagPoll(object? sender, EventArgs e)
    {
        bool wantError = File.Exists(Path.Combine(_dataDir, "fault.robot.error"));
        if (wantError != _server.State.HasError)
        {
            if (wantError) InjectError(); else ClearError();
        }

        bool wantNoVerify = File.Exists(Path.Combine(_dataDir, "fault.robot.noverify"));
        if (wantNoVerify != _server.State.SuppressWaferUpdate)
        {
            _server.State.SuppressWaferUpdate = wantNoVerify;
            Log("故障注入", wantNoVerify ? "取放片不更新手指在位" : "手指在位更新已恢复");
        }
    }

    // ── 服务器生命周期 ───────────────────────────────────────────

    private void WireServerEvents()
    {
        _server.OnLog += msg => Log("系统", msg);
        _server.OnCommandReceived += (ep, cmd) => Log("接收", $"{ep}  {cmd}");
        _server.OnResponseSent += (ep, rsp) => Log(ep == "(推送)" ? "推送" : "发送", $"{ep}  {rsp}");
        _server.OnClientConnected += ep => Log("系统", "客户端已连接: " + ep);
        _server.OnClientDisconnected += ep => Log("系统", "客户端已断开: " + ep);
    }

    private void StartServer()
    {
        if (_server.IsRunning) return;

        ApplyToolParams();
        _server.Port = ParseInt(PortText.Text, _server.Port, 1, 65535);
        Log("系统", $"正在监听 0.0.0.0:{_server.Port} ...");
        _ = Task.Run(() => _server.StartAsync());
        UpdateServerUi();
    }

    private void StopServer()
    {
        if (!_server.IsRunning) return;
        _server.Stop();
        UpdateServerUi();
    }

    /// <summary>停表 + 停监听, 释放全部资源 (宿主契约; 关页签/关窗口时调用)。</summary>
    public void ShutdownForHost()
    {
        _server.State.ArmWaferChanged -= OnStateArmWaferChanged;   // 谁 += 谁 -=
        _refresh.Stop();
        _heartbeat.Stop();
        _faultPoll.Stop();
        _server.Stop();
    }

    /// <summary>工具栏四个延迟/失败率参数即时写入服务器 (输入合法才写)。</summary>
    private void ApplyToolParams()
    {
        _server.AckDelayMs = ParseInt(AckDelayText.Text, _server.AckDelayMs, 0, 60000);
        _server.ResponseDelayMs = ParseInt(RspDelayText.Text, _server.ResponseDelayMs, 0, 60000);
        _server.MotionHandler.SimulatedDelayMs = ParseInt(MotionDelayText.Text, _server.MotionHandler.SimulatedDelayMs, 0, 60000);
        _server.MotionHandler.FailureRate = ParseInt(FailRateText.Text, _server.MotionHandler.FailureRate, 0, 100);
    }

    private static int ParseInt(string text, int fallback, int min, int max)
    {
        if (int.TryParse(text.Trim(), out int v))
            return Math.Clamp(v, min, max);
        return fallback;
    }

    // ── 错误注入 / 主动上报 ───────────────────────────────────────

    private void InjectError()
    {
        _server.State.HasError = true;
        _server.State.ErrorCode = "12345678";
        _server.State.ErrorDesc = "Simulated error";
        Log("系统", "已注入模拟错误 [12345678]");

        // 协议: 主动报错开启时，Robot 主动上报错误帧
        if (_server.State.ActiveErrorEnabled && _server.IsRunning)
        {
            _server.Broadcast(ResponseBuilder.BuildFailure("Error", "12345678", "Simulated error"));
        }
    }

    private void ClearError()
    {
        _server.State.HasError = false;
        _server.State.ErrorCode = "00000000";
        _server.State.ErrorDesc = "";
        Log("系统", "错误已清除");
    }

    // ── 定时刷新: 回填状态控件 + 快照 + 状态栏 + 心跳同步 ────────

    private void RefreshUi()
    {
        var st = _server.State;

        _suppress = true;
        ChkEnable.IsChecked = st.IsEnabled;
        if (CboMode.SelectedIndex != st.OpMode - 1 && st.OpMode >= 1 && st.OpMode <= 3)
            CboMode.SelectedIndex = st.OpMode - 1;
        ChkW1.IsChecked = st.Arm1HasWafer;
        ChkW2.IsChecked = st.Arm2HasWafer;
        ChkW3.IsChecked = st.Arm3HasWafer;
        ChkW4.IsChecked = st.Arm4HasWafer;
        ChkP1.IsChecked = st.Arm1Pressure;
        ChkP2.IsChecked = st.Arm2Pressure;
        ChkP3.IsChecked = st.Arm3Pressure;
        ChkP4.IsChecked = st.Arm4Pressure;
        ChkSubscribe.IsChecked = st.WaferSubscribed;
        ChkActiveError.IsChecked = st.ActiveErrorEnabled;
        ChkDeadMan.IsChecked = st.DeadManErrorEnabled;
        ChkSlideDetect.IsChecked = st.SlideDetectEnabled;

        // 数值区回填 (协议命令 Home/G/Home 也会改这些值, 面板要跟着走; 正在编辑的框不回填)
        SetNum(NumSpeed, st.SpeedPercent);
        SetAxisNum(NumX, st.PosX);
        SetAxisNum(NumZ, st.PosZ);
        SetAxisNum(NumTheta, st.PosTheta);
        SetAxisNum(NumAux, st.PosAux);
        SetAxisNum(NumArm1, st.PosArm1);
        SetAxisNum(NumArm2, st.PosArm2);
        SetAxisNum(NumArm3, st.PosArm3);
        SetAxisNum(NumArm4, st.PosArm4);
        SetAxisNum(NumFlip1, st.PosFlip1);
        SetAxisNum(NumFlip2, st.PosFlip2);
        _suppress = false;

        SyncHeartbeat();
        UpdateSnapshot(st);
        UpdateServerUi();
    }

    /// <summary>按 OpenStartHeart/HeartTime 设置同步心跳定时器（开关 + 间隔）。</summary>
    private void SyncHeartbeat()
    {
        bool shouldRun = _server.IsRunning && _server.State.HeartbeatEnabled;
        int interval = Math.Max(1, _server.State.HeartbeatIntervalMs);
        if (_heartbeat.Interval != TimeSpan.FromMilliseconds(interval))
            _heartbeat.Interval = TimeSpan.FromMilliseconds(interval);
        if (shouldRun && !_heartbeat.IsEnabled) _heartbeat.Start();
        else if (!shouldRun && _heartbeat.IsEnabled) _heartbeat.Stop();
    }

    /// <summary>
    /// 手指在位变化 → 订阅状态下立即推送 SubWaferEx（0=有片, 1=无片）。
    /// 事件可能来自 TCP 后台线程; Broadcast 内部有写锁, 跨线程安全, 不编组到 UI。
    /// 订阅时的初始在位基线由 TcpServer.ProcessCommand 收到 SubWafer1 时补推, 不在此处。
    /// </summary>
    private void OnStateArmWaferChanged(int armNo, bool has)
    {
        var st = _server.State;
        if (!st.WaferSubscribed || !_server.IsRunning)
        {
            return;
        }
        string content = $"SubWaferEx,{armNo},{(has ? 0 : 1)}";
        _server.Broadcast(ResponseBuilder.BuildEvent(content));
    }

    private void UpdateSnapshot(State.RobotState st)
    {
        string mode = st.OpMode switch { 1 => "示教", 2 => "自动", 3 => "远程", _ => "未知" };

        SnapError.Text = st.HasError ? $"{st.ErrorCode}  {st.ErrorDesc}" : "无";
        SnapError.Foreground = st.HasError
            ? (Brush)FindResource("DarkAlarmStatus")
            : (Brush)FindResource("DarkPrimaryText");
        SnapExec.Text = st.IsExecuting ? "是" : "否";
        SnapEnable.Text = st.IsEnabled ? "是" : "否";
        SnapMode.Text = mode;
        SnapSpeed.Text = $"{st.SpeedPercent} %";
        SnapDistance.Text = $"{st.ArmDistance} mm";
        SnapProject.Text = $"{st.ProjectName} / {st.ProgramName}";
        SnapHeart.Text = st.HeartbeatEnabled ? $"开 {st.HeartbeatIntervalMs} ms" : "关";
        SnapWafer.Text = $"1={Has(st.Arm1HasWafer)} 2={Has(st.Arm2HasWafer)} 3={Has(st.Arm3HasWafer)} 4={Has(st.Arm4HasWafer)}";
        SnapPressure.Text = $"1={YN(st.Arm1Pressure)} 2={YN(st.Arm2Pressure)} 3={YN(st.Arm3Pressure)} 4={YN(st.Arm4Pressure)}";
        SnapEmv.Text = $"1={OC(st.EMV1)} 2={OC(st.EMV2)} 3={OC(st.EMV3)} 4={OC(st.EMV4)}";
        SnapSub.Text = st.WaferSubscribed ? "是" : "否";
        SnapAeo.Text = $"AEO {OC(st.ActiveErrorEnabled)}  DMO {OC(st.DeadManErrorEnabled)}  SWO {OC(st.SlideDetectEnabled)}";
        SnapZRatio.Text = $"取 {st.ZFetchSpeedRatio}% / 放 {st.ZLoadSpeedRatio}%";
        SnapPosX.Text = st.PosX.ToString("F1");
        SnapPosZ.Text = st.PosZ.ToString("F1");
        SnapPosTheta.Text = st.PosTheta.ToString("F1");
        SnapPosAux.Text = st.PosAux.ToString("F1");
        SnapPosArm12.Text = $"{st.PosArm1:F1} / {st.PosArm2:F1}";
        SnapPosArm34.Text = $"{st.PosArm3:F1} / {st.PosArm4:F1}";
        SnapPosFlip.Text = $"{st.PosFlip1:F1} / {st.PosFlip2:F1}";
    }

    private static string Has(bool b) => b ? "有" : "无";
    private static string YN(bool b) => b ? "Y" : "N";
    private static string OC(bool b) => b ? "开" : "关";

    private void UpdateServerUi()
    {
        bool run = _server.IsRunning;
        var on = (Brush)FindResource("DarkCommunicationStatus");
        var off = (Brush)FindResource("DarkAlarmStatus");
        HeaderDot.Fill = run ? on : off;
        StatusDot.Fill = run ? on : off;
        HeaderText.Text = run ? $"监听中 0.0.0.0:{_server.Port}" : "未监听";
        StatusText.Text = (run ? "● 监听中   " : "○ 未监听   ") + $"端口={_server.Port}";
        StatusParam.Text = $"动作耗时={_server.MotionHandler.SimulatedDelayMs}ms   失败率={_server.MotionHandler.FailureRate}%   ACK/结果延迟={_server.AckDelayMs}/{_server.ResponseDelayMs}ms";
        StatusDataDir.Text = "数据目录: " + _dataDir;
        PortText.IsEnabled = !run;
        BtnStart.IsEnabled = !run;
        BtnStop.IsEnabled = run;
    }

    // ── 状态拨动区事件 ───────────────────────────────────────────

    private void OnEnableChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _server.State.IsEnabled = ChkEnable.IsChecked == true;
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || CboMode.SelectedIndex < 0) return;
        _server.State.OpMode = CboMode.SelectedIndex + 1;
    }

    private void OnSpeedChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        if (int.TryParse(NumSpeed.Text.Trim(), out int v))
            _server.State.SpeedPercent = Math.Clamp(v, 1, 100);
    }

    private void OnArmWaferToggled(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        var box = (CheckBox)sender;
        if (!int.TryParse(box.Tag?.ToString(), out int arm)) return;
        switch (arm)
        {
            case 1: _server.State.Arm1HasWafer = box.IsChecked == true; break;
            case 2: _server.State.Arm2HasWafer = box.IsChecked == true; break;
            case 3: _server.State.Arm3HasWafer = box.IsChecked == true; break;
            case 4: _server.State.Arm4HasWafer = box.IsChecked == true; break;
        }
    }

    private void OnArmPressureChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        var box = (CheckBox)sender;
        bool on = box.IsChecked == true;
        switch (box.Tag?.ToString())
        {
            case "1": _server.State.Arm1Pressure = on; break;
            case "2": _server.State.Arm2Pressure = on; break;
            case "3": _server.State.Arm3Pressure = on; break;
            case "4": _server.State.Arm4Pressure = on; break;
        }
    }

    private void OnAxisPosChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        var box = (TextBox)sender;
        if (!double.TryParse(box.Text.Trim(), out double v)) return;
        SetAxisPosByTag(box.Tag?.ToString() ?? "", v);
    }

    private void SetAxisPosByTag(string axis, double v)
    {
        switch (axis.ToUpperInvariant())
        {
            case "X": _server.State.PosX = v; break;
            case "Z": _server.State.PosZ = v; break;
            case "THETA": _server.State.PosTheta = v; break;
            case "AUX": _server.State.PosAux = v; break;
            case "ARM1": _server.State.PosArm1 = v; break;
            case "ARM2": _server.State.PosArm2 = v; break;
            case "ARM3": _server.State.PosArm3 = v; break;
            case "ARM4": _server.State.PosArm4 = v; break;
            case "FLIP1": _server.State.PosFlip1 = v; break;
            case "FLIP2": _server.State.PosFlip2 = v; break;
        }
    }

    private void OnSubscribeChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _server.State.WaferSubscribed = ChkSubscribe.IsChecked == true;
    }

    private void OnToggleSwitchChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        bool on = ((CheckBox)sender).IsChecked == true;
        switch (((CheckBox)sender).Tag?.ToString())
        {
            case "ActiveError": _server.State.ActiveErrorEnabled = on; break;
            case "DeadMan": _server.State.DeadManErrorEnabled = on; break;
            case "SlideDetect": _server.State.SlideDetectEnabled = on; break;
        }
    }

    private void OnQuietChanged(object sender, RoutedEventArgs e)
    {
        _quietLog = ChkQuiet.IsChecked == true;
    }

    // ── 工具栏按钮 ───────────────────────────────────────────────

    private void OnToolParamChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady || _suppress) return;
        ApplyToolParams();
        UpdateServerUi();
    }

    private void BtnStart_Click(object sender, RoutedEventArgs e) => StartServer();

    private void BtnStop_Click(object sender, RoutedEventArgs e) => StopServer();

    private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
    {
        var cfg = _config.Config;
        cfg.Port = ParseInt(PortText.Text, cfg.Port, 1, 65535);
        cfg.AckDelayMs = ParseInt(AckDelayText.Text, cfg.AckDelayMs, 0, 60000);
        cfg.ResponseDelayMs = ParseInt(RspDelayText.Text, cfg.ResponseDelayMs, 0, 60000);
        cfg.MotionDelayMs = ParseInt(MotionDelayText.Text, cfg.MotionDelayMs, 0, 60000);
        cfg.FailureRate = ParseInt(FailRateText.Text, cfg.FailureRate, 0, 100);
        _config.SaveConfig();
        Log("系统", "配置已保存: " + Path.Combine(_dataDir, "config.json"));
    }

    private void BtnProtocolTest_Click(object sender, RoutedEventArgs e)
    {
        Log("自测", "开始协议逻辑自测 (不依赖 TCP)...");
        new ProtocolTest(msg => Log("自测", msg)).Run();
    }

    private void BtnInjectError_Click(object sender, RoutedEventArgs e) => InjectError();

    private void BtnClearError_Click(object sender, RoutedEventArgs e) => ClearError();

    private void BtnResetState_Click(object sender, RoutedEventArgs e)
    {
        _server.State.Reset();
        Log("系统", "机器人状态已重置");
    }

    private void BtnManualSend_Click(object sender, RoutedEventArgs e)
    {
        string frame = ManualFrameText.Text.Trim();
        if (string.IsNullOrEmpty(frame))
        {
            Log("系统", "主动广播帧内容为空");
            return;
        }
        if (!_server.IsRunning)
        {
            Log("系统", "未启动监听, 帧未广播; 先点[启动监听]");
            return;
        }
        _server.Broadcast(frame);
    }

    // ── 日志（跨线程封送到 UI） ──────────────────────────────────

    private static readonly Dictionary<string, Brush> TagBrushes = new()
    {
        ["接收"] = Freeze("#4CAF50"),
        ["发送"] = Freeze("#42A5F5"),
        ["推送"] = Freeze("#CE93D8"),
        ["系统"] = Freeze("#9E9E9E"),
        ["错误"] = Freeze("#EF5350"),
        ["自测"] = Freeze("#FFC107"),
        ["故障注入"] = Freeze("#FF9800"),
    };

    private static Brush Freeze(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    /// <summary>写一条日志。任意线程可调, 自动封送 UI; 同时落盘 log.txt 供无人值守排查。</summary>
    private void Log(string tag, string msg)
    {
        if (_quietLog)
        {
            return;   // 在编组到 UI 线程之前就丢弃, 否则 BeginInvoke 照样淹 UI 线程
        }
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => Log(tag, msg));
            return;
        }
        TagBrushes.TryGetValue(tag, out var brush);
        Logs.Add(new LogEntry(
            DateTime.Now.ToString("HH:mm:ss.fff"),
            tag,
            msg,
            brush ?? Brushes.Gray));

        try
        {
            File.AppendAllText(Path.Combine(_dataDir, "log.txt"),
                $"{DateTime.Now:HH:mm:ss.fff}  {tag}  {msg}{Environment.NewLine}");
        }
        catch
        {
            // 只读目录等场景日志落盘失败可忽略
        }
    }

    // ── 数值回填小工具 ───────────────────────────────────────────

    /// <summary>回填数值框: 正在编辑(有焦点)不动, 值没变不动。</summary>
    private static void SetNum(TextBox box, double value, string format = "0")
    {
        if (box.IsKeyboardFocused) return;
        string text = value.ToString(format);
        if (box.Text != text) box.Text = text;
    }

    private static void SetAxisNum(TextBox box, double value)
    {
        SetNum(box, value, "0.0##");
    }
}

/// <summary>日志一条 (收发日志区绑定模型)。</summary>
public record LogEntry(string Time, string Tag, string Message, Brush TagBrush);
