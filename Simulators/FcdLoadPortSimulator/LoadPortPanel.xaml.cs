using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FcdLoadPortSimulator;

/// <summary>
/// FcdLoadPort (富创得 LP300) 仿真器面板。
///
/// 核心是一张可编辑的"应答表": 每行一条上位机指令, 收到后照表里配置的
///   第二列 = 第一次回复(ACK)
///   第三列 = 第二次回复(INF 完成 / ABS 异常)
/// 依次发出 (自动套 B 类帧壳 s00...; + CR)。后两列双击即可修改 ——
/// 想模拟报错, 把"类型"列切成 ABS 即可; GET 这类单段回复第二次列留空。
/// </summary>
public partial class LoadPortPanel : UserControl
{
    // ── B 类帧常量 ──
    private const string FramePrefix = "s00";  // SOH('s') + ADR("00")
    private const char FrameEnd = ';';
    private const char FrameDel = '\r';        // DEL = 0x0D
    private const int MultiFrameGapMs = 200;   // 第二次回复含多帧时的帧间隔

    // 带 25 槽 mapping 数据的指令: MOV 类在 INF 列, GET 类在 ACK 列
    private static readonly string[] MapMovCmds = { "MOV:CLOAD", "MOV:CLDMP", "MOV:CLMPO" };
    private static readonly string[] MapGetCmds = { "GET:MAPDT", "GET:MAPRD" };

    private readonly ConfigManager _config;
    private readonly string _dataDir;
    private readonly string _responsesPath;
    private readonly StringBuilder _rxBuffer = new();
    // 指令(TYPE:NAME) -> 行号, 收到时 O(1) 定位
    private readonly Dictionary<string, int> _cmdRow = new(StringComparer.OrdinalIgnoreCase);

    private SerialPort? _serialPort;
    private DispatcherTimer? _faultPoll;   // 故障注入旗标轮询, ShutdownForHost 里停
    private bool _faultGarbage;
    private bool _faultNoInf;
    private bool _syncingFoupUi;   // 程序同步勾选状态时抑制 CheckedChanged 写回

    /// <summary>应答表行集合 (DataGrid 绑定)。</summary>
    public ObservableCollection<ResponseRow> Rows { get; } = new();

    /// <summary>收发日志集合 (ListBox 绑定)。</summary>
    public ObservableCollection<LogEntry> Logs { get; } = new();

    // ── 宿主契约成员 (独立薄窗口与 SimulatorHub 页签共用同一面板) ──

    /// <summary>实例名(取自数据目录末段), 供宿主拼页签标题/定位实例目录。</summary>
    public string InstanceName { get; }

    /// <summary>当前串口下拉框的串口名 (如 COM3); 空串=未选。仅改选择, 不开串口。</summary>
    public string SelectedPort
    {
        get => PortCombo.Text.Trim();
        set => PortCombo.Text = (value ?? "").Trim();
    }

    /// <summary>
    /// 打开当前选中的串口。silent 保留给宿主契约统一签名 —— 本面板打开失败本就只写日志不弹窗,
    /// 批量应用预设时不会打断流程。
    /// </summary>
    public void OpenSelectedPort(bool silent) => OpenPort();

    /// <summary>宿主驱动的关停: 停故障注入轮询 + 关串口。页签关闭/独立窗口 Closing 时调。</summary>
    public void ShutdownForHost()
    {
        _faultPoll?.Stop();
        ClosePort();
    }

    public LoadPortPanel(string dataDir)
    {
        _dataDir = dataDir;
        _config = new ConfigManager(dataDir);
        _responsesPath = Path.Combine(dataDir, "responses.json");

        InitializeComponent();
        DataContext = this;

        // 崩溃钩子由宿主 App 统一注册(一进程一份), 面板里不再挂

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

        LoadPortList();
        LoadResponses();                    // 从 responses.json 读取(不存在则写入默认)
        SyncFoupUiFromState();              // 从 GET:STATE 应答读回勾选框状态
        UpdateStatus();

        // 故障注入旗标轮询 (无人值守冒烟用): 数据目录下放旗标文件即生效, 删掉即恢复
        _faultPoll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _faultPoll.Tick += OnFaultFlagPoll;
        _faultPoll.Start();
    }

    // ── 故障注入 ─────────────────────────────────────────────────
    //   fault.devicealarm  勾上"硬件报警(byte8)", 等价于人手点那个勾选框
    //   fault.garbage      应答改发乱码帧, 让上位机解析失败
    //   fault.noinf        只回 ACK 不回 INF 完成, 让上位机的动作一直等不到终态
    // 这些文件都不存在时行为与加这段之前完全一致。

    private void OnFaultFlagPoll(object? sender, EventArgs e)
    {
        bool wantAlarm = File.Exists(Path.Combine(_dataDir, "fault.devicealarm"));
        if (wantAlarm != (ChkFoupAlarm.IsChecked == true))
        {
            ChkFoupAlarm.IsChecked = wantAlarm;   // 触发 CheckedChanged, 写回 GET:STATE 应答
            Log("故障注入", wantAlarm ? "硬件报警(byte8) 已置位" : "硬件报警(byte8) 已清除");
        }

        bool wantNoInf = File.Exists(Path.Combine(_dataDir, "fault.noinf"));
        if (wantNoInf != _faultNoInf)
        {
            _faultNoInf = wantNoInf;
            Log("故障注入", wantNoInf ? "只回 ACK 不回 INF 完成" : "INF 完成回复已恢复");
        }

        bool wantGarbage = File.Exists(Path.Combine(_dataDir, "fault.garbage"));
        if (wantGarbage != _faultGarbage)
        {
            _faultGarbage = wantGarbage;
            Log("故障注入", wantGarbage ? "应答改发乱码帧" : "应答恢复正常");
        }
    }

    // ── 应答表构建 ───────────────────────────────────────────────

    /// <summary>按手册支持的指令构建默认应答表; 带 mapping 的指令默认回 25 槽全有片(P)。</summary>
    private static List<ResponseRow> BuildDefaults()
    {
        var list = new List<ResponseRow>();
        string map = new('P', 25);           // 都有片
        string state = BuildState64();
        string output = new('F', 64);        // 全无信号

        // 带 mapping 的动作: 真机先单独推一帧 INF:MAPDT/<map>, 再推空载荷的 INF:<动作名> 收尾。
        // 第二次回复列用 '|' 分隔多帧, 依次发出。
        void Mov(string name, string note, string? m)
        {
            string inf = m == null ? "INF:" + name : "INF:MAPDT/" + m + "|INF:" + name;
            list.Add(new ResponseRow { Cmd = "MOV:" + name, Note = note, Ack = "ACK:" + name, Inf = inf, Abs = "ABS:" + name + "/0301", Type = "INF" });
        }
        void Row(string type, string name, string note, string ack, string inf, string abs)
        {
            list.Add(new ResponseRow { Cmd = type + ":" + name, Note = note, Ack = ack, Inf = inf, Abs = abs, Type = "INF" });
        }

        // MOV 运动指令: ACK 受理 → INF 完成 (带 mapping 的附 25 槽 map)
        Mov("ORGSH", "整机回零", null);
        Mov("CLOAD", "Load: 开门 + mapping", map);
        Mov("CULOD", "Unload: 关门", null);
        Mov("CLDMP", "Mapping 动作", map);
        Mov("CULDK", "门升起并关上", null);
        Mov("CULYD", "Latch 上锁并松气", null);
        Mov("CUDCL", "Undock, Table 退出", null);
        Mov("CLDYD", "Table 推进, 准备 unlatch", null);
        Mov("CLDOP", "真空吸 / 门吸住 / unlatch 解锁", null);
        Mov("CLMPO", "开门并 Mapping", map);
        Mov("PODCL", "Lock FOUP", null);
        Mov("PODOP", "Unlock FOUP", null);
        Mov("RESUM", "恢复", null);
        Mov("PAUSE", "暂停", null);
        Mov("ABORT", "终止", null);

        // SET 设置指令: ACK → INF
        Row("SET", "RESET", "复位", "ACK:RESET", "INF:RESET", "ABS:RESET/0301");
        Row("SET", "OUPUT", "设置输出", "ACK:OUPUT", "INF:OUPUT", "ABS:OUPUT/0301");
        Row("SET", "E84EN", "E84 激活", "ACK:E84EN", "INF:E84EN", "ABS:E84EN/0301");
        Row("SET", "E84ES", "E84 扩展设置", "ACK:E84ES", "INF:E84ES", "ABS:E84ES/0301");

        // GET 查询指令: 单段 ACK + 数据 (无第二次回复, INF/ABS 均留空)
        Row("GET", "VERSN", "版本信息", "ACK:VERSN/02-04-04-LP300SIM", "", "");
        Row("GET", "STATE", "系统状态(64)", "ACK:STATE/" + state, "", "");
        Row("GET", "MAPDT", "Mapping 数据(正序)", "ACK:MAPDT/" + map, "", "");
        Row("GET", "MAPRD", "Mapping 数据(逆序)", "ACK:MAPRD/" + map, "", "");
        Row("GET", "OUPUT", "输出状态(64)", "ACK:OUPUT/" + output, "", "");
        return list;
    }

    /// <summary>用给定行集填充应答表(清空重建), 并重建 指令→行号 索引。</summary>
    private void FillTable(List<ResponseRow> rows)
    {
        Rows.Clear();
        _cmdRow.Clear();
        foreach (var r in rows)
        {
            string cmd = (r.Cmd ?? "").Trim();
            if (cmd.Length == 0) continue;
            Rows.Add(r);
            _cmdRow[cmd] = Rows.Count - 1;
        }
        StatusRowCount.Text = $"应答表 {Rows.Count} 行";
    }

    /// <summary>把当前应答表导出为可持久化的行集(供保存)。</summary>
    private List<ResponseRow> DumpTable() => Rows.ToList();

    // ── 应答表配置 读 / 写 / 重载 ───────────────────────────────

    /// <summary>启动加载: 有 responses.json 就读它, 没有就用默认并写出一份。</summary>
    private void LoadResponses()
    {
        if (ResponseStore.Exists(_responsesPath))
        {
            try
            {
                var rows = ResponseStore.Load(_responsesPath);
                if (rows.Count == 0) rows = BuildDefaults();
                FillTable(rows);
                Log("系统", "应答表已从配置加载: " + _responsesPath);
            }
            catch (Exception ex)
            {
                FillTable(BuildDefaults());
                Log("错误", "读取 responses.json 失败, 已用默认: " + ex.Message);
            }
        }
        else
        {
            var rows = BuildDefaults();
            FillTable(rows);
            try
            {
                ResponseStore.Save(_responsesPath, rows);
                Log("系统", "未找到配置, 已写入默认: " + _responsesPath);
            }
            catch (Exception ex)
            {
                Log("错误", "写入默认配置失败: " + ex.Message);
            }
        }
        UpdateStatus();
    }

    private void SaveResponses()
    {
        try
        {
            ResponseGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ResponseGrid.CommitEdit(DataGridEditingUnit.Row, true);
            ResponseStore.Save(_responsesPath, DumpTable());
            Log("系统", "应答表已保存: " + _responsesPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), "保存失败:\n" + ex.Message, "LP300 仿真器", MessageBoxButton.OK, MessageBoxImage.Error);
            Log("错误", "保存失败: " + ex.Message);
        }
    }

    private void ReloadResponses()
    {
        if (!ResponseStore.Exists(_responsesPath))
        {
            FillTable(BuildDefaults());
            SyncFoupUiFromState();
            Log("系统", "配置文件不存在, 已载入默认(未保存)");
            return;
        }
        try
        {
            FillTable(ResponseStore.Load(_responsesPath));
            SyncFoupUiFromState();
            Log("系统", "已从文件重新加载: " + _responsesPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), "重新加载失败:\n" + ex.Message, "LP300 仿真器", MessageBoxButton.OK, MessageBoxImage.Error);
            Log("错误", "重新加载失败: " + ex.Message);
        }
    }

    private void RestoreDefaults()
    {
        var ok = MessageBox.Show(Window.GetWindow(this), "恢复默认应答表? (当前界面内容会被覆盖, 点[保存配置]后才会写入文件)",
            "LP300 仿真器", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ok != MessageBoxResult.OK) return;
        FillTable(BuildDefaults());
        SyncFoupUiFromState();
        Log("系统", "已恢复默认(未保存)");
    }

    /// <summary>一键把所有带 mapping 的回复置成 25 槽同一字符 (如全 P / 全 E), 只改界面不落盘。</summary>
    private void SetMappingAll(char fill)
    {
        ResponseGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ResponseGrid.CommitEdit(DataGridEditingUnit.Row, true);
        string map = new(fill, 25);
        foreach (string cmd in MapMovCmds)
        {
            if (_cmdRow.TryGetValue(cmd, out int idx))
            {
                Rows[idx].Inf = "INF:" + cmd[4..] + "/" + map;
            }
        }
        foreach (string cmd in MapGetCmds)
        {
            if (_cmdRow.TryGetValue(cmd, out int idx))
            {
                Rows[idx].Ack = "ACK:" + cmd[4..] + "/" + map;
            }
        }
        Log("系统", $"Mapping 已全部置为 '{fill}' x25 (CLOAD/CLDMP/CLMPO/MAPDT/MAPRD, 未保存, 点[保存配置]才写入文件)");
    }

    private static string BuildState64()
    {
        char[] s = new char[64];
        for (int i = 0; i < s.Length; i++) s[i] = '0';
        s[0] = '1'; // Pod Presence
        s[1] = '1'; // PIP Placement
        s[3] = '1'; // Table Out
        return new string(s);
    }

    // ── FOUP 状态设置区 ───────────────────────────────────────────

    /// <summary>勾选框变化: 写回 GET:STATE 应答 (仅界面, 不落盘)。</summary>
    private void OnFoupCheckChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingFoupUi) return;
        ApplyFoupToState();
    }

    /// <summary>
    /// 当前 GET:STATE 应答里的 64 字符状态串; 无 GET:STATE 行 / 格式异常用默认打底,
    /// 保住其他字节用户可能手改过的值。
    /// </summary>
    private string CurrentState64()
    {
        if (_cmdRow.TryGetValue("GET:STATE", out int idx))
        {
            string ack = Rows[idx].Ack ?? "";
            int slash = ack.IndexOf('/');
            if (slash >= 0)
            {
                string data = ack[(slash + 1)..];
                if (data.Length == 64) return data;
            }
        }
        return BuildState64();
    }

    /// <summary>按勾选框组合出 64 字符状态串: byte1 在位 / byte2 放好 / byte8 硬件报警。</summary>
    private string ComposeState64()
    {
        char[] s = CurrentState64().ToCharArray();
        s[0] = ChkFoupPresent.IsChecked == true ? '1' : '0';
        s[1] = ChkFoupPlaced.IsChecked == true ? '1' : '0';
        s[7] = ChkFoupAlarm.IsChecked == true ? '1' : '0';
        return new string(s);
    }

    /// <summary>把勾选框组合写进 GET:STATE 应答 (仅界面)。</summary>
    private void ApplyFoupToState()
    {
        if (!_cmdRow.TryGetValue("GET:STATE", out int idx))
        {
            Log("系统", "应答表无 GET:STATE 行, FOUP 状态未应用");
            return;
        }
        Rows[idx].Ack = "ACK:STATE/" + ComposeState64();
        string alarm = ChkFoupAlarm.IsChecked == true ? ", 硬件报警" : "";
        Log("系统", $"FOUP 状态: 在位={(ChkFoupPresent.IsChecked == true ? "1" : "0")} 放好={(ChkFoupPlaced.IsChecked == true ? "1" : "0")}{alarm} (GET:STATE, 未保存)");
    }

    /// <summary>从 GET:STATE 应答读回勾选框状态 (启动/重载/恢复默认后同步界面)。</summary>
    private void SyncFoupUiFromState()
    {
        _syncingFoupUi = true;
        try
        {
            string state = CurrentState64();
            ChkFoupPresent.IsChecked = state[0] == '1';
            ChkFoupPlaced.IsChecked = state[1] == '1';
            ChkFoupAlarm.IsChecked = state[7] == '1';
        }
        finally
        {
            _syncingFoupUi = false;
        }
    }

    /// <summary>放/取 FOUP: 先同步 GET:STATE 应答(防周期查询把事件结果顶回去), 再发主动事件; 串口未开只提示。</summary>
    private void SendPodEvent(string evt, bool present)
    {
        _syncingFoupUi = true;
        try
        {
            ChkFoupPresent.IsChecked = present;
            ChkFoupPlaced.IsChecked = present;
        }
        finally
        {
            _syncingFoupUi = false;
        }
        ApplyFoupToState();
        if (_serialPort is null || !_serialPort.IsOpen)
        {
            Log("系统", "串口未打开, 事件未发送; 先点[打开]");
            return;
        }
        WriteFrame(evt);
        Log("系统", "已发送主动事件 " + evt);
    }

    // ── 串口 ─────────────────────────────────────────────────────

    private void LoadPortList()
    {
        string selected = PortCombo.Text;
        PortCombo.Items.Clear();
        foreach (var p in SerialPort.GetPortNames())
        {
            PortCombo.Items.Add(p);
        }
        string def = _config.Config.SerialPort.DefaultPort ?? "";
        if (!string.IsNullOrEmpty(selected) && PortCombo.Items.Contains(selected))
        {
            PortCombo.SelectedItem = selected;
        }
        else if (!string.IsNullOrEmpty(def))
        {
            if (!PortCombo.Items.Contains(def)) PortCombo.Items.Add(def);
            PortCombo.SelectedItem = def;
        }
        else if (PortCombo.Items.Count > 0)
        {
            PortCombo.SelectedIndex = 0;
        }
    }

    private void OpenPort()
    {
        ClosePort();
        string port = PortCombo.Text.Trim();
        if (string.IsNullOrEmpty(port))
        {
            Log("系统", "未指定串口, 跳过自动打开");
            return;
        }
        try
        {
            var cfg = _config.Config.SerialPort;
            var stop = cfg.DefaultStopBits == 2 ? StopBits.Two : StopBits.One;
            var sp = new SerialPort(port, cfg.DefaultBaudRate, ParseParity(cfg.DefaultParity), cfg.DefaultDataBits, stop)
            {
                Encoding = Encoding.ASCII,
            };
            sp.DataReceived += SerialPort_DataReceived;
            sp.Open();
            _serialPort = sp;
            _rxBuffer.Clear();
            Log("系统", $"串口已打开: {port} @ {cfg.DefaultBaudRate}");
        }
        catch (Exception ex)
        {
            Log("错误", "打开串口失败: " + ex.Message);
        }
        UpdateStatus();
    }

    private void ClosePort()
    {
        var sp = _serialPort;
        _serialPort = null;
        if (sp is null) return;
        try
        {
            sp.DataReceived -= SerialPort_DataReceived;
            if (sp.IsOpen) sp.Close();
            sp.Dispose();
            Log("系统", "串口已关闭");
        }
        catch (Exception ex)
        {
            Log("错误", "关闭串口出错: " + ex.Message);
        }
    }

    private static Parity ParseParity(string? p) => (p ?? "None").ToUpperInvariant() switch
    {
        "E" or "EVEN" => Parity.Even,
        "O" or "ODD" => Parity.Odd,
        "M" or "MARK" => Parity.Mark,
        "S" or "SPACE" => Parity.Space,
        _ => Parity.None,
    };

    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var sp = _serialPort;
            if (sp is null) return;
            _rxBuffer.Append(sp.ReadExisting());

            // 按 CR 切帧, 完整帧封送到 UI 线程做表驱动应答
            string buffered = _rxBuffer.ToString();
            int idx;
            while ((idx = buffered.IndexOf(FrameDel)) >= 0)
            {
                string frame = buffered[..idx];
                buffered = buffered[(idx + 1)..];
                if (!string.IsNullOrWhiteSpace(frame))
                {
                    Dispatcher.BeginInvoke(() => HandleFrame(frame));
                }
            }
            _rxBuffer.Clear();
            _rxBuffer.Append(buffered);
        }
        catch (Exception ex)
        {
            Log("错误", "接收出错: " + ex.Message);
        }
    }

    // ── 表驱动应答 ───────────────────────────────────────────────

    /// <summary>处理一帧上位机指令 (UI 线程): 立即回 ACK, 延时后回 INF/ABS (可多帧)。</summary>
    private void HandleFrame(string raw)
    {
        string cmd = StripFrame(raw);
        Log("接收", raw.Trim());

        int colon = cmd.IndexOf(':');
        if (colon != 3)
        {
            WriteFrame("NAK:" + cmd + "/0107");   // 命令类型非3字符 → 参数错误
            return;
        }

        string type = cmd[..3];
        string body = cmd[(colon + 1)..];
        string name = body.Split('/')[0];
        string key = type + ":" + name;

        if (!_cmdRow.TryGetValue(key, out int rowIdx))
        {
            WriteFrame("NAK:" + name + "/0103");  // 指令不支持
            return;
        }

        // 门信号副作用: 真机 Load 后 DoorOpen(byte43) 一直有, Unload/Home/Reset 后没有。
        // 表驱动应答无状态机, 在收到动作指令时同步改写 GET:STATE 应答串, RT 周期查询即可读到。
        ApplyDoorStateSideEffect(type, name);

        ResponseRow row = Rows[rowIdx];
        string ack = row.Ack ?? "";
        // 第二次回复发哪条由"类型"列决定: ABS → ABS 列; 否则 INF 列
        string replyType = (row.Type ?? "INF").Trim();
        string second = replyType.Equals("ABS", StringComparison.OrdinalIgnoreCase)
            ? (row.Abs ?? "")
            : (row.Inf ?? "");
        BumpHit(rowIdx);

        if (!string.IsNullOrEmpty(ack))
        {
            WriteFrame(ack);
        }
        if (!string.IsNullOrEmpty(second) && !_faultNoInf)
        {
            int delay = GetInfDelay();
            // 第二次回复可以是多帧 ('|' 分隔), 例如 Load 先推 INF:MAPDT 再推 INF:CLOAD
            string[] frames = second.Split('|');
            _ = Task.Run(async () =>
            {
                if (delay > 0) await Task.Delay(delay);
                for (int i = 0; i < frames.Length; i++)
                {
                    string part = frames[i].Trim();
                    if (part.Length == 0) continue;
                    if (i > 0) await Task.Delay(MultiFrameGapMs);
                    WriteFrame(part);
                }
            });
        }
    }

    /// <summary>
    /// 真机门信号语义: Load(CLOAD/CLMPO 开门) 后 DoorOpen 一直保持到 Unload(CULOD 关门);
    /// Home(ORGSH) / Reset(RESET) / 关门动作(CULDK) 也会把门关到位。
    /// 表驱动应答没有状态机, 这里把 GET:STATE 的 byte43(DoorOpen)/byte44(DoorClose) 一并改写,
    /// 让 RT 侧 1s 周期查询拿到的门状态和真机一致 (其余指令不动门)。
    /// </summary>
    private void ApplyDoorStateSideEffect(string type, string name)
    {
        bool? doorOpen = null;
        if (type == "MOV")
        {
            switch (name)
            {
                case "CLOAD":
                case "CLMPO":       // 开门并 Mapping
                    doorOpen = true;
                    break;
                case "CULOD":       // Unload 关门
                case "CULDK":       // 门升起并关上
                case "ORGSH":       // 整机回零
                    doorOpen = false;
                    break;
            }
        }
        else if (type == "SET" && name == "RESET")
        {
            doorOpen = false;
        }

        if (doorOpen == null) return;
        SetDoorSensors((bool)doorOpen, name);
    }

    /// <summary>把门开/关到位传感器写进 GET:STATE 应答串 (byte43=开, byte44=关, 互补)。</summary>
    private void SetDoorSensors(bool open, string trigger)
    {
        if (!_cmdRow.TryGetValue("GET:STATE", out int idx)) return;

        char[] s = CurrentState64().ToCharArray();
        s[42] = open ? 'O' : 'F';   // byte43 DoorOpen: 'O'=门开到位
        s[43] = open ? 'F' : 'O';   // byte44 DoorClose: 'O'=门关到位
        Rows[idx].Ack = "ACK:STATE/" + new string(s);
        Log("系统", $"{trigger} → 门{(open ? "开" : "关")}到位 (DoorOpen={s[42]}, DoorClose={s[43]}, GET:STATE 已更新)");
    }

    /// <summary>去掉 B 类外壳 (s+ADR 前缀 / 结尾 ';' / 历史 "$1" 前缀)。</summary>
    private static string StripFrame(string raw)
    {
        string s = raw.Trim();
        if (s.StartsWith("$1")) s = s[2..];
        if (s.Length >= 3 && s[0] == 's') s = s[3..];
        if (s.EndsWith(";")) s = s[..^1];
        return s.Trim();
    }

    /// <summary>把回复体 (如 "ACK:CLOAD") 套上帧壳后发出。任意线程可调。</summary>
    private void WriteFrame(string body)
    {
        var sp = _serialPort;
        if (sp is null || !sp.IsOpen || string.IsNullOrEmpty(body)) return;
        string frame = FramePrefix + body + FrameEnd + FrameDel;
        if (_faultGarbage)
        {
            frame = "@@GARBAGE@@" + FrameDel;
        }
        try
        {
            sp.Write(frame);
            Log("发送", _faultGarbage ? "@@GARBAGE@@" : FramePrefix + body + FrameEnd);
        }
        catch (Exception ex)
        {
            Log("错误", "发送失败: " + ex.Message);
        }
    }

    // ── UI 小工具 ────────────────────────────────────────────────

    private int GetInfDelay()
    {
        return int.TryParse(InfDelayText.Text.Trim(), out int ms) ? Math.Clamp(ms, 0, 60000) : 1000;
    }

    private void BumpHit(int rowIdx)
    {
        if (rowIdx >= Rows.Count) return;
        Rows[rowIdx].Bump();
        ResponseGrid.SelectedIndex = rowIdx;
        ResponseGrid.ScrollIntoView(Rows[rowIdx]);
    }

    private void ResetHits()
    {
        foreach (var row in Rows)
        {
            row.ResetHits();
        }
    }

    private static readonly Dictionary<string, Brush> TagBrushes = new()
    {
        ["接收"] = Freeze("#4CAF50"),
        ["发送"] = Freeze("#42A5F5"),
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

    private void UpdateStatus()
    {
        bool open = _serialPort is { IsOpen: true };
        var on = (Brush)FindResource("DarkCommunicationStatus");
        var off = (Brush)FindResource("DarkAlarmStatus");
        HeaderDot.Fill = open ? on : off;
        StatusDot.Fill = open ? on : off;
        string port = open ? _serialPort!.PortName : "(未打开)";
        int baud = _config.Config.SerialPort.DefaultBaudRate;
        HeaderText.Text = open ? $"串口已打开 {port}" : "串口未打开";
        StatusText.Text = (open ? "● 已打开   " : "○ 未打开   ") + $"串口={port}   波特率={baud}";
        StatusRowCount.Text = $"应答表 {Rows.Count} 行";
        StatusDataDir.Text = "数据目录: " + _dataDir;
    }

    // ── 按钮 ─────────────────────────────────────────────────────

    private void BtnRefreshPorts_Click(object sender, RoutedEventArgs e) => LoadPortList();

    private void BtnOpen_Click(object sender, RoutedEventArgs e) => OpenPort();

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        ClosePort();
        UpdateStatus();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e) => SaveResponses();

    private void BtnReload_Click(object sender, RoutedEventArgs e) => ReloadResponses();

    private void BtnDefault_Click(object sender, RoutedEventArgs e) => RestoreDefaults();

    private void BtnMapAllP_Click(object sender, RoutedEventArgs e) => SetMappingAll('P');

    private void BtnMapAllE_Click(object sender, RoutedEventArgs e) => SetMappingAll('E');

    private void BtnResetHits_Click(object sender, RoutedEventArgs e)
    {
        ResetHits();
        Log("系统", "命中计数已清零");
    }

    private void BtnProtocolTest_Click(object sender, RoutedEventArgs e)
    {
        Log("自测", "开始协议逻辑自测 (不依赖串口)...");
        new ProtocolTest(msg => Log("自测", msg)).Run();
    }

    private void BtnPodOn_Click(object sender, RoutedEventArgs e) => SendPodEvent("INF:PODON", true);

    private void BtnPodOff_Click(object sender, RoutedEventArgs e) => SendPodEvent("INF:PODOF", false);

    private void BtnManualSend_Click(object sender, RoutedEventArgs e)
    {
        string body = ManualFrameText.Text.Trim();
        if (string.IsNullOrEmpty(body))
        {
            Log("系统", "主动发帧内容为空");
            return;
        }
        if (_serialPort is null || !_serialPort.IsOpen)
        {
            Log("系统", "串口未打开, 帧未发送; 先点[打开]");
            return;
        }
        WriteFrame(body);
    }
}

/// <summary>日志一条 (收发日志区绑定模型)。</summary>
public record LogEntry(string Time, string Tag, string Message, Brush TagBrush);
