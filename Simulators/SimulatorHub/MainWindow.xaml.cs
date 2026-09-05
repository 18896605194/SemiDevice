using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

using FcdLoadPortSimulator;
using RejeRobotSimulator;

namespace SimulatorHub;

/// <summary>
/// 统一仿真器壳: 每个页签内嵌一台仿真器面板 (RobotPanel / LoadPortPanel, 与各自
/// 独立 exe 共用同一实现)。宿主用委托捕获各类型, 不搞共享接口 —— 以后接入新仿真器
/// (如 PLC)就是: csproj 加引用 + 工具栏加按钮 + CreateTab 加一个分支。
///
/// 生命周期归宿主管: 加页签即启动 (监听/开串口), 关页签调 Cleanup; 不挂面板的
/// Loaded/Unloaded —— 页签切换会触发 Unloaded, 生命周期绝不能绑在可见性上。
/// 布局: 实例目录 instances\&lt;名&gt;, 命名预设 profiles\*.json, 退出/每次变更写
/// last-layout.json, 启动自动静默还原。
/// </summary>
public partial class MainWindow : Window
{
    private const string TypeRobot = "Robot";
    private const string TypeLp300 = "Lp300";
    private const int DefaultRobotPort = 9000;

    /// <summary>一个页签 = 一台仿真实例。委托按类型各配一套, 壳不依赖面板具体类。</summary>
    private sealed class SimTab
    {
        public required string Type { get; init; }
        public required string InstanceName { get; init; }
        public required FrameworkElement Panel { get; init; }
        public required Func<string> GetPort { get; init; }
        public required Action<string> SetPort { get; init; }
        public required Action Open { get; init; }
        public required Action Cleanup { get; init; }
        public required TextBlock HeaderText { get; init; }
        public bool AutoOpen = true;
    }

    private readonly string _instancesDir;
    private readonly string _profilesDir;
    private readonly string _lastLayoutPath;
    private readonly string _lastPresetPath;
    private readonly List<SimTab> _tabs = new();
    private readonly DispatcherTimer _headerRefresh = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow(string dataDir)
    {
        InitializeComponent();
        ClampToWorkArea();

        _instancesDir = Path.Combine(dataDir, "instances");
        _profilesDir = Path.Combine(dataDir, "profiles");
        _lastLayoutPath = Path.Combine(dataDir, "last-layout.json");
        _lastPresetPath = Path.Combine(dataDir, "last-preset.txt");
        Directory.CreateDirectory(_instancesDir);
        Directory.CreateDirectory(_profilesDir);

        StatusDir.Text = "数据: " + dataDir;

        _headerRefresh.Tick += (_, _) => RefreshHeaders();   // 面板内改端口后页签标题跟着变
        _headerRefresh.Start();

        Closing += (_, _) => OnExit();

        EnsureBuiltinPreset();

        // 启动自动还原上次布局 (静默, 不弹确认)
        if (LayoutStore.Load(_lastLayoutPath) is { Instances.Count: > 0 } last)
        {
            ApplyInstances(last.Instances);
            SetStatus($"已还原上次布局: {last.Instances.Count} 个实例");
        }
        UpdateEmptyState();
        UpdateStatus();
    }

    // ── 添加实例 ─────────────────────────────────────────────────

    private void BtnAddRobot_Click(object sender, RoutedEventArgs e)
    {
        var tab = CreateTab(TypeRobot, NewInstanceName(TypeRobot));
        tab.SetPort(NextFreeRobotPort(DefaultRobotPort).ToString());
        AddTab(tab);
        TryOpen(tab);
        SaveLastLayout();
        SetStatus($"已添加 {HeaderOf(tab)} — 开始监听");
    }

    private void BtnAddLp_Click(object sender, RoutedEventArgs e)
    {
        var tab = CreateTab(TypeLp300, NewInstanceName(TypeLp300));
        AddTab(tab);   // 串口取实例 config 默认, 不强改
        TryOpen(tab);
        SaveLastLayout();
        SetStatus($"已添加 {HeaderOf(tab)}");
    }

    private SimTab CreateTab(string type, string instanceName)
    {
        string dir = Path.Combine(_instancesDir, instanceName);
        Directory.CreateDirectory(dir);

        if (type == TypeRobot)
        {
            var p = new RobotPanel(dir);
            return new SimTab
            {
                Type = type,
                InstanceName = instanceName,
                Panel = p,
                GetPort = () => p.ListenPort.ToString(),
                SetPort = s => { if (int.TryParse(s.Trim(), out int v)) p.ListenPort = v; },
                Open = p.StartListening,
                Cleanup = p.ShutdownForHost,
                HeaderText = NewHeader(),
            };
        }

        var lp = new LoadPortPanel(dir);
        return new SimTab
        {
            Type = type,
            InstanceName = instanceName,
            Panel = lp,
            GetPort = () => lp.SelectedPort,
            SetPort = s => lp.SelectedPort = s,
            Open = () => lp.OpenSelectedPort(silent: true),
            Cleanup = lp.ShutdownForHost,
            HeaderText = NewHeader(),
        };
    }

    private TextBlock NewHeader() => new() { Style = (Style)FindResource("TabHead") };

    private void AddTab(SimTab tab, bool select = true)
    {
        tab.HeaderText.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount >= 2) CloseTabWithConfirm(tab);   // 双击标题=关闭, 同 XM
        };
        var item = new TabItem { Content = tab.Panel, Header = tab.HeaderText };
        item.SetResourceReference(StyleProperty, "SimTabItem");
        Tabs.Items.Add(item);
        _tabs.Add(tab);
        RefreshTabHeader(tab);
        if (select) Tabs.SelectedItem = item;
        UpdateEmptyState();
        UpdateStatus();
    }

    private static void TryOpen(SimTab tab)
    {
        try { tab.Open(); }
        catch { /* 面板内部已记日志 (如串口不存在/端口被占) */ }
    }

    // ── 关闭实例 ─────────────────────────────────────────────────

    private void BtnCloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (Tabs.SelectedItem is TabItem item && item.Header is TextBlock header)
        {
            var tab = _tabs.FirstOrDefault(t => ReferenceEquals(t.HeaderText, header));
            if (tab is not null) CloseTabWithConfirm(tab);
        }
    }

    private void CloseTabWithConfirm(SimTab tab)
    {
        var ok = MessageBox.Show(this,
            $"关闭页签 {HeaderOf(tab)}?\n实例数据保留在 {Path.Combine(_instancesDir, tab.InstanceName)}, 随时可再添加。",
            "xyz 统一仿真器", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;
        RemoveTab(tab);
        SaveLastLayout();
    }

    private void RemoveTab(SimTab tab)
    {
        try { tab.Cleanup(); }
        catch { /* 关停异常不挡壳 */ }
        if (tab.Panel.Parent is TabItem item) Tabs.Items.Remove(item);
        _tabs.Remove(tab);
        UpdateEmptyState();
        UpdateStatus();
    }

    private void OnExit()
    {
        try { SaveLastLayout(); }
        catch { /* 落盘失败不挡退出 */ }
        foreach (var t in _tabs.ToList())
        {
            try { t.Cleanup(); }
            catch { }
        }
        _tabs.Clear();
        _headerRefresh.Stop();
    }

    // ── 编号与端口 ───────────────────────────────────────────────

    private static string PrefixOf(string type) => type == TypeRobot ? "robot" : "lp";

    private static string DisplayOf(string type) => type == TypeRobot ? "Robot" : type == TypeLp300 ? "LP300" : type;

    /// <summary>新实例名 = 前缀-(现有最大编号+1); 同时看打开着的页签和磁盘目录, 还原布局后也不重号。</summary>
    private string NewInstanceName(string type)
    {
        string prefix = PrefixOf(type);
        int max = 0;
        int Scan(string name)
        {
            if (name.Length <= prefix.Length + 1 || !name.StartsWith(prefix + "-", StringComparison.OrdinalIgnoreCase)) return 0;
            return int.TryParse(name[(prefix.Length + 1)..], out int v) ? v : 0;
        }
        foreach (var t in _tabs) max = Math.Max(max, Scan(t.InstanceName));
        try
        {
            foreach (var d in Directory.EnumerateDirectories(_instancesDir)) max = Math.Max(max, Scan(Path.GetFileName(d)));
        }
        catch { /* 目录枚举失败就只按页签编号 */ }
        return $"{prefix}-{max + 1}";
    }

    /// <summary>默认 9000 起, 已被其它机械手页签占用的端口自动 +1。</summary>
    private int NextFreeRobotPort(int start)
    {
        var used = new HashSet<int>();
        foreach (var t in _tabs)
        {
            if (t.Type == TypeRobot && int.TryParse(t.GetPort(), out int v)) used.Add(v);
        }
        int port = start;
        while (used.Contains(port)) port++;
        return port;
    }

    private static string HeaderOf(SimTab t)
    {
        string n = t.InstanceName;
        int dash = n.LastIndexOf('-');
        string num = dash >= 0 && int.TryParse(n[(dash + 1)..], out int v) ? v.ToString() : n;
        return $"{DisplayOf(t.Type)} #{num} · {t.GetPort()}";
    }

    private void RefreshTabHeader(SimTab t) => t.HeaderText.Text = HeaderOf(t);

    private void RefreshHeaders()
    {
        foreach (var t in _tabs) RefreshTabHeader(t);
    }

    // ── 布局预设 ─────────────────────────────────────────────────

    private string ProfilePath(string name) => Path.Combine(_profilesDir, name + ".json");

    private LayoutProfile CaptureLayout() => new()
    {
        Instances = _tabs.Select(t => new InstanceLayout
        {
            Type = t.Type,
            Instance = t.InstanceName,
            Port = t.GetPort(),
            AutoOpen = t.AutoOpen,
        }).ToList(),
    };

    private void SaveLastLayout()
    {
        try
        {
            var p = CaptureLayout();
            p.Name = "last";
            LayoutStore.Save(_lastLayoutPath, p);
        }
        catch { /* 磁盘问题不挡界面 */ }
    }

    /// <summary>首次运行且无任何预设时写入内置"标准"预设: 单机械手 + 单 LoadPort。</summary>
    private void EnsureBuiltinPreset()
    {
        try
        {
            if (Directory.Exists(_profilesDir) && Directory.GetFiles(_profilesDir, "*.json").Length > 0) return;
            LayoutStore.Save(ProfilePath("标准"), new LayoutProfile
            {
                Name = "标准",
                Instances =
                {
                    new InstanceLayout { Type = TypeRobot, Instance = "robot-1", Port = DefaultRobotPort.ToString(), AutoOpen = true },
                    new InstanceLayout { Type = TypeLp300, Instance = "lp-1", Port = "", AutoOpen = true },   // 端口空=用实例默认
                },
            });
        }
        catch { }
    }

    private void BtnSaveLayout_Click(object sender, RoutedEventArgs e)
    {
        if (_tabs.Count == 0)
        {
            SetStatus("没有页签可保存");
            return;
        }
        string? name = NamePromptDialog.Show(this, "保存布局预设", "预设名称:", "预设-" + DateTime.Now.ToString("MMdd-HHmm"));
        if (string.IsNullOrWhiteSpace(name)) return;
        name = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (name.Length == 0) return;

        var p = CaptureLayout();
        p.Name = name;
        try
        {
            LayoutStore.Save(ProfilePath(name), p);
            File.WriteAllText(_lastPresetPath, name);
            SetStatus($"预设 [{name}] 已保存 ({p.Instances.Count} 个实例)");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存预设失败:\n" + ex.Message, "xyz 统一仿真器", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>启用预设… = 按最近使用的预设打开勾选对话框; 没用过预设就退到上次布局。</summary>
    private void BtnApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? last = File.Exists(_lastPresetPath) ? File.ReadAllText(_lastPresetPath).Trim() : null;
            if (!string.IsNullOrEmpty(last) && File.Exists(ProfilePath(last)))
            {
                OpenPresetPicker(last);
                return;
            }
        }
        catch { /* 读失败走下面的兜底 */ }

        var layout = LayoutStore.Load(_lastLayoutPath);
        if (layout is { Instances.Count: > 0 })
        {
            var dlg = new LayoutPickerDialog("上次布局", layout, allowDelete: false) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                ApplyInstances(dlg.Selected);
                SetStatus($"已应用上次布局: {dlg.Selected.Count} 个实例");
            }
            return;
        }
        SetStatus("还没有预设 — 先 [保存布局]");
    }

    private void BtnLoadPreset_Click(object sender, RoutedEventArgs e)
    {
        PresetMenu.Items.Clear();

        var miLast = new MenuItem { Header = "应用上次布局 (直接还原)" };
        miLast.Click += (_, _) =>
        {
            var layout = LayoutStore.Load(_lastLayoutPath);
            if (layout is { Instances.Count: > 0 })
            {
                ApplyInstances(layout.Instances);
                SetStatus($"已还原上次布局: {layout.Instances.Count} 个实例");
            }
            else SetStatus("没有上次布局");
        };
        PresetMenu.Items.Add(miLast);
        PresetMenu.Items.Add(new Separator());

        string[] files = Array.Empty<string>();
        try { files = Directory.GetFiles(_profilesDir, "*.json"); }
        catch { }
        if (files.Length == 0)
        {
            PresetMenu.Items.Add(new MenuItem { Header = "(无已存预设 — 先 [保存布局])", IsEnabled = false });
        }
        else
        {
            foreach (var f in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                var mi = new MenuItem { Header = name };
                mi.Click += (_, _) => OpenPresetPicker(name);
                PresetMenu.Items.Add(mi);
            }
        }

        PresetMenu.PlacementTarget = BtnLoadPreset;
        PresetMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        PresetMenu.IsOpen = true;
    }

    private void OpenPresetPicker(string name)
    {
        var profile = LayoutStore.Load(ProfilePath(name));
        if (profile is null || profile.Instances.Count == 0)
        {
            SetStatus($"预设 [{name}] 为空或损坏");
            return;
        }
        var dlg = new LayoutPickerDialog(name, profile, allowDelete: true) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        if (dlg.DeleteRequested)
        {
            try
            {
                File.Delete(ProfilePath(name));
                if (File.Exists(_lastPresetPath) && File.ReadAllText(_lastPresetPath).Trim() == name) File.Delete(_lastPresetPath);
                SetStatus($"预设 [{name}] 已删除");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "删除预设失败:\n" + ex.Message, "xyz 统一仿真器", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return;
        }

        ApplyInstances(dlg.Selected);
        try { File.WriteAllText(_lastPresetPath, name); }
        catch { }
        SetStatus($"已启用预设 [{name}]: {dlg.Selected.Count} 个实例");
    }

    /// <summary>应用一组实例: 关掉现有全部页签 (不逐个确认), 按布局重建并按需自动开口。</summary>
    private void ApplyInstances(List<InstanceLayout> instances)
    {
        foreach (var t in _tabs.ToList()) RemoveTab(t);
        foreach (var il in instances)
        {
            string name = string.IsNullOrWhiteSpace(il.Instance) ? NewInstanceName(il.Type) : il.Instance;
            var tab = CreateTab(il.Type, name);
            if (!string.IsNullOrWhiteSpace(il.Port)) tab.SetPort(il.Port);
            tab.AutoOpen = il.AutoOpen;
            AddTab(tab, select: false);
            if (il.AutoOpen) TryOpen(tab);
        }
        if (Tabs.Items.Count > 0) Tabs.SelectedIndex = 0;
        SaveLastLayout();
        UpdateEmptyState();
        UpdateStatus();
    }

    // ── 状态条 ───────────────────────────────────────────────────

    private void SetStatus(string text) => StatusText.Text = text;

    private void UpdateStatus()
    {
        int robots = _tabs.Count(t => t.Type == TypeRobot);
        int lps = _tabs.Count(t => t.Type == TypeLp300);
        var parts = new List<string>();
        if (robots > 0) parts.Add($"机械手 ×{robots}");
        if (lps > 0) parts.Add($"LoadPort ×{lps}");
        if (parts.Count > 0) StatusText.Text = string.Join("   ", parts) + "   ·   双击页签标题可关闭";
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = _tabs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    /// <summary>
    /// 目标 1280x720，但不超过主屏工作区 90%——高 DPI 小屏上不再铺满整屏。
    /// </summary>
    private void ClampToWorkArea()
    {
        var work = SystemParameters.WorkArea;
        if (Width > work.Width * 0.9)
        {
            Width = work.Width * 0.9;
        }

        if (Height > work.Height * 0.9)
        {
            Height = work.Height * 0.9;
        }
    }
}
