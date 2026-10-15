using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Configs;
using xyz.Configs.Models;

namespace xyz.Components.Components;

/// <summary>
/// IO 组件：点表 + 按索引读写。
/// </summary>
[Component(description: "IO 组件（点表 + 按索引读写）")]
public class IoComponent : ComponentBase
{
    /// <summary>
    /// 当前 IO 表；sc.xml 里装出来即生效，上层可按索引取点。
    /// </summary>
    public static IoComponent? Current { get; set; }

    public IoComponent()
    {
        Current = this;
    }

    /// <summary>
    /// 本组件用的 PLC。Current 装配/冒烟里会后设，所以摸一次就固定，不放构造里绑。
    /// </summary>
    private IPlc? _plc;

    private IPlc? Plc => _plc ??= PlcComponent.Current;

    #region SC

    [SCEditor("IO", "Io", "点表目录（相对 sc.xml 所在目录），里面放 DI.csv / DO.csv / AI.csv / AO.csv")]
    public string PointTableDirectory { get; set; } = "IO";

    [SCEditor("", "Io", "开机下发的 DO：连上 PLC 后按这里写一遍，如 DO12=false;DO13=true，多个用分号隔开，DO 不分大小写；空 = 不下发")]
    public string StartupDo { get; set; } = string.Empty;

    [SCEditor("", "Io", "安全联锁（SC）：Di25=false:DO12=true,DO13=true;... 分号隔规则，逗号隔 DO；条件成立时 DO 强制为配置值、相反时强制为相反值，下发不一致就拒绝并给原因；空 = 不启用")]
    public string SafetyInterlock { get; set; } = string.Empty;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "100", "10000", "500", "IO 界面推送周期")]
    public int PublishIntervalMs
    {
        get { return GetEcInt(nameof(PublishIntervalMs)); }
        set { SetEcInt(nameof(PublishIntervalMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "0", "60000", "1000", "连上 PLC 后隔多久下发开机 DO")]
    public int StartupDoDelayMs
    {
        get { return GetEcInt(nameof(StartupDoDelayMs)); }
        set { SetEcInt(nameof(StartupDoDelayMs), value); }
    }

    #endregion

    #region 安全联锁规则

    /// <summary>解析后的 SC 规则；空 = 不启用。</summary>
    private IReadOnlyList<IoInterlockRule> _interlockRules = [];

    /// <summary>DO 索引  约束它的规则。下发前按这个表查，SC 自己强制时也按它算目标值。</summary>
    private Dictionary<int, List<IoInterlockRule>> _interlockRulesByDo = new();

    /// <summary>同一目标值写失败后隔多久再试，避免 PLC 没连稳时每拍都打。</summary>
    private const int InterlockRetryMs = 500;

    /// <summary>SC 最近一次准备强制到的值：DO 索引  值。</summary>
    private readonly Dictionary<int, bool> _interlockLastValue = new();

    /// <summary>SC 最近一次强制下发的时间：DO 索引  TickCount64。</summary>
    private readonly Dictionary<int, long> _interlockLastAttempt = new();

    /// <summary>已经报过配置冲突的 DO，避免每拍刷日志。</summary>
    private readonly HashSet<int> _interlockConflictLogged = new();

    #endregion

    /// <summary>
    /// 装配校验：开机 DO / 安全联锁写错了开机就报出来，不等连上 PLC 才发现没生效。
    /// </summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);

        var startupDo = new List<(int Index, bool On)>();
        foreach (string item in StartupDo.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = item.Split('=', StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !parts[0].StartsWith("DO", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(parts[0][2..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                || !bool.TryParse(parts[1], out bool on))
            {
                throw new InvalidOperationException(
                    $"{Name}：StartupDo 里的{item}写得不对，要写成 DO12=false，多个用分号隔开");
            }

            startupDo.Add((index, on));
        }

        _startupDo = startupDo;

        try
        {
            _interlockRules = IoInterlockParser.Parse(SafetyInterlock);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"{Name}：SafetyInterlock {exception.Message}");
        }

        _interlockRulesByDo = BuildInterlockRulesByDo(_interlockRules);
    }

    #region SV

    /// <summary>
    /// 采集是否在工作（SV）：PLC 连着才算。断了以后所有点都读不到（TryRead 一律 false），不会拿陈旧值当真。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "IO 采集是否在工作")]
    public bool IsCollecting => Plc?.IsConnected ?? false;

    #endregion

    #region 点表

    public IoPointTable Di { get; } = new("DI");

    public IoPointTable Do { get; } = new("DO");

    public IoPointTable Ai { get; } = new("AI");

    public IoPointTable Ao { get; } = new("AO");

    /// <summary>
    /// 读点表；由装配在模块启动前调用。点表不在就是空表，照常空转不拦启动
    /// 装机时电控还没给点表是常事。
    /// </summary>
    public bool Open()
    {
        // 全限定：组件基类自己有个 Path 属性（组件路径），这儿的 Path 不是它。
        string directory = System.IO.Path.Combine(SC.ConfigDirectory, PointTableDirectory);

        Di.Load(System.IO.Path.Combine(directory, "DI.csv"));
        Do.Load(System.IO.Path.Combine(directory, "DO.csv"));
        Ai.Load(System.IO.Path.Combine(directory, "AI.csv"));
        Ao.Load(System.IO.Path.Combine(directory, "AO.csv"));

        ValidateInterlock();

        LogHelper.Info($"[{FullPath}] 点表装载：DI {Di.Points.Count}、DO {Do.Points.Count}、"
                       + $"AI {Ai.Points.Count}、AO {Ao.Points.Count} 点（{directory}）");
        return true;
    }

    #endregion

    #region 安全联锁

    private static Dictionary<int, List<IoInterlockRule>> BuildInterlockRulesByDo(IReadOnlyList<IoInterlockRule> rules)
    {
        var map = new Dictionary<int, List<IoInterlockRule>>();
        foreach (var rule in rules)
        {
            foreach (var action in rule.Actions)
            {
                if (!map.TryGetValue(action.DoIndex, out var list))
                {
                    list = [];
                    map[action.DoIndex] = list;
                }

                list.Add(rule);
            }
        }

        return map;
    }

    /// <summary>
    /// 点表装载后校验 SC 引用的点：DO 必须在 DO 点表里；DI 要么有点、要么有对应的 DI 监控组件。
    /// 安全联锁引用不存在的点属于装机配置错误，直接拦启动，不能悄悄放过。
    /// </summary>
    private void ValidateInterlock()
    {
        var warnedDi = new HashSet<int>();
        foreach (var rule in _interlockRules)
        {
            foreach (var action in rule.Actions)
            {
                if (Do.Find(action.DoIndex) is null)
                {
                    throw new InvalidOperationException(
                        $"{Name}：安全联锁引用的 DO{action.DoIndex} 不在 DO 点表里（规则：{rule.Text}）");
                }
            }

            if (Di.Find(rule.DiIndex) is not null)
            {
                if (!DiSensorComponent.TryGetByIndex(rule.DiIndex, out _) && warnedDi.Add(rule.DiIndex))
                {
                    LogHelper.Warn($"[{FullPath}] 安全联锁规则 {rule.Text}：DI{rule.DiIndex} 没有对应的 DI 监控组件，按 DI 点表原始电平判断");
                }

                continue;
            }

            if (DiSensorComponent.TryGetByIndex(rule.DiIndex, out _))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"{Name}：安全联锁引用的 DI{rule.DiIndex} 既不在 DI 点表里，也没有对应的 DI 监控组件（规则：{rule.Text}）");
        }
    }

    /// <summary>
    /// 每个扫描周期把受 SC 约束的 DO 拉回 DI 状态对应的值：
    /// Di 条件成立  强制为配置值；条件不成立  强制为配置值的相反值。
    /// </summary>
    private void EnforceInterlock()
    {
        if (_interlockRules.Count == 0)
        {
            return;
        }

        var plc = Plc;
        if (plc is null || !plc.IsConnected)
        {
            return;
        }

        var desired = new Dictionary<int, bool>();
        var desiredRule = new Dictionary<int, IoInterlockRule>();
        var conflicts = new HashSet<int>();

        foreach (var rule in _interlockRules)
        {
            bool? active = TryGetDiCondition(rule, out _, out _);
            foreach (var action in rule.Actions)
            {
                if (active is null)
                {
                    _interlockLastValue.Remove(action.DoIndex);
                    _interlockLastAttempt.Remove(action.DoIndex);
                    continue;
                }

                bool required = active.Value ? action.Value : !action.Value;
                if (desired.TryGetValue(action.DoIndex, out bool existing) && existing != required)
                {
                    conflicts.Add(action.DoIndex);
                    desiredRule[action.DoIndex] = rule;
                    continue;
                }

                desired[action.DoIndex] = required;
                if (!desiredRule.ContainsKey(action.DoIndex))
                {
                    desiredRule[action.DoIndex] = rule;
                }
            }
        }

        long now = Environment.TickCount64;
        foreach (var (doIndex, required) in desired)
        {
            if (conflicts.Contains(doIndex))
            {
                _interlockLastValue.Remove(doIndex);
                _interlockLastAttempt.Remove(doIndex);
                if (_interlockConflictLogged.Add(doIndex))
                {
                    LogHelper.Error($"[{FullPath}] 安全联锁配置冲突：DO{doIndex} 同时被多条规则要求不同值，已停止强制并拦截该 DO 的下发");
                }

                continue;
            }

            bool readbackOk = TryReadDo(doIndex, out bool current);
            if (readbackOk && current == required)
            {
                _interlockLastValue.Remove(doIndex);
                _interlockLastAttempt.Remove(doIndex);
                _interlockConflictLogged.Remove(doIndex);
                continue;
            }

            bool valueChanged = !_interlockLastValue.TryGetValue(doIndex, out bool last) || last != required;
            if (!valueChanged
                && _interlockLastAttempt.TryGetValue(doIndex, out long lastAttempt)
                && now - lastAttempt < InterlockRetryMs)
            {
                continue;
            }

            _interlockLastAttempt[doIndex] = now;
            if (ForceWriteDo(doIndex, required))
            {
                if (valueChanged)
                {
                    LogHelper.Info($"[{FullPath}] 安全联锁 {desiredRule[doIndex].Text}：强制 DO{doIndex}={(required ? "true" : "false")}");
                }

                _interlockLastValue[doIndex] = required;
                _interlockConflictLogged.Remove(doIndex);
            }
            else
            {
                if (valueChanged)
                {
                    LogHelper.Warn($"[{FullPath}] 安全联锁 {desiredRule[doIndex].Text}：强制 DO{doIndex}={(required ? "true" : "false")} 失败");
                }

                _interlockLastValue[doIndex] = required;
            }
        }

        foreach (int doIndex in _interlockConflictLogged.Where(index => !conflicts.Contains(index)).ToArray())
        {
            _interlockConflictLogged.Remove(doIndex);
        }
    }

    /// <summary>
    /// 读一条规则的 DI 条件是否成立。优先用被监控 DI 的 DiSensorComponent 最近一次原始电平，
    /// 找不到组件或组件还没读到值时退回 IoComponent 自己的点表读法；读不到返回 null（调用方按安全优先处理）。
    /// </summary>
    private bool? TryGetDiCondition(IoInterlockRule rule, out string diName, out bool? diLevel)
    {
        diName = DiDisplayName(rule.DiIndex);
        if (DiSensorComponent.TryGetByIndex(rule.DiIndex, out var sensor) && sensor is not null)
        {
            diName = string.IsNullOrWhiteSpace(sensor.FullPath) ? sensor.Name : sensor.FullPath;
            if (sensor.CurrentLevel is bool level)
            {
                diLevel = level;
                return level == rule.DiValue;
            }
        }

        if (TryReadDi(rule.DiIndex, out bool on))
        {
            diLevel = on;
            return on == rule.DiValue;
        }

        diLevel = null;
        return null;
    }

    private string DiDisplayName(int index)
    {
        var point = Di.Find(index);
        return point is null || string.IsNullOrWhiteSpace(point.Name) ? $"DI{index}" : $"DI{index}({point.Name})";
    }

    /// <summary>
    /// 下发前查 SC：只要 DO 被规则约束，请求值就必须等于当前 DI 状态对应的要求值；
    /// 读不到 DI 或配置本身冲突时按安全优先拦截。
    /// </summary>
    private IoInterlockBlock? CheckInterlock(int doIndex, bool requested)
    {
        if (!_interlockRulesByDo.TryGetValue(doIndex, out var rules) || rules.Count == 0)
        {
            return null;
        }

        var requirements = new List<(IoInterlockRule Rule, bool Value, string DiName, bool? DiLevel)>();
        foreach (var rule in rules)
        {
            var action = rule.Actions.First(item => item.DoIndex == doIndex);
            bool? active = TryGetDiCondition(rule, out string diName, out bool? diLevel);
            if (active is null)
            {
                return new IoInterlockBlock(IoInterlockBlockKind.Unknown, doIndex, requested,
                    rule.DiIndex, diName, null, null, rule.Text);
            }

            requirements.Add((rule, active.Value ? action.Value : !action.Value, diName, diLevel));
        }

        if (requirements.Select(item => item.Value).Distinct().Count() > 1)
        {
            return new IoInterlockBlock(IoInterlockBlockKind.Conflict, doIndex, requested,
                rules[0].DiIndex, string.Empty, null, null, string.Join("; ", rules.Select(item => item.Text)));
        }

        var requirement = requirements[0];
        if (requested == requirement.Value)
        {
            return null;
        }

        return new IoInterlockBlock(IoInterlockBlockKind.Blocked, doIndex, requested,
            requirement.Rule.DiIndex, requirement.DiName, requirement.DiLevel, requirement.Value, requirement.Rule.Text);
    }

    #endregion

    #region 按索引读写（点表里没有该索引、PLC 没连或读不到时一律 false）

    /// <summary>
    /// 读一个 DI 点。
    /// </summary>
    public bool TryReadDi(int index, out bool on)
    {
        on = false;
        var point = Di.Find(index);
        var plc = Plc;
        if (point is null || plc is null)
        {
            return false;
        }

        return plc.TryReadDi(point.Index, out on);
    }

    /// <summary>
    /// 回读一个 DO 点当前的输出状态。
    /// </summary>
    public bool TryReadDo(int index, out bool on)
    {
        on = false;
        var point = Do.Find(index);
        var plc = Plc;
        if (point is null || plc is null)
        {
            return false;
        }

        return plc.TryReadDo(point.Index, out on);
    }

    /// <summary>
    /// 写一个 DO 点：先过安全联锁，再写 PLC；被联锁拦下时返回 false。
    /// 需要知道拦下的原因（UI 回错误码）用 <see cref="TryWriteDo"/>。
    /// </summary>
    public bool WriteDo(int index, bool on)
    {
        return TryWriteDo(index, on, out _);
    }

    /// <summary>
    /// 写一个 DO 点并返回被安全联锁拦下的详情；block 为 null 且返回 false 表示 PLC 没连上、点表没有这个点或写 PLC 失败。
    /// </summary>
    public bool TryWriteDo(int index, bool on, out IoInterlockBlock? block)
    {
        block = null;
        var point = Do.Find(index);
        var plc = Plc;
        if (point is null || plc is null || !plc.IsConnected)
        {
            return false;
        }

        block = CheckInterlock(index, on);
        if (block is not null)
        {
            return false;
        }

        return plc.WriteDo(point.Index, on);
    }

    /// <summary>
    /// SC 强制下发：目标值本来就是联锁要求的值，绕过下发前的联锁检查，避免自己拦自己。
    /// </summary>
    private bool ForceWriteDo(int index, bool on)
    {
        var point = Do.Find(index);
        var plc = Plc;
        if (point is null || plc is null || !plc.IsConnected)
        {
            return false;
        }

        return plc.WriteDo(point.Index, on);
    }

    /// <summary>
    /// 读一个 AI 点的工程值（按点表里的标定换算；没标定就是原始码）。
    /// </summary>
    public bool TryReadAi(int index, out double value)
    {
        value = 0;
        var point = Ai.Find(index);
        var plc = Plc;
        if (point is null || plc is null || !plc.TryReadAi(point.Index, out double raw))
        {
            return false;
        }

        value = point.ToEngineering(raw);
        return true;
    }

    /// <summary>
    /// 回读一个 AO 点当前的输出工程值。
    /// </summary>
    public bool TryReadAo(int index, out double value)
    {
        value = 0;
        var point = Ao.Find(index);
        var plc = Plc;
        if (point is null || plc is null || !plc.TryReadAo(point.Index, out double raw))
        {
            return false;
        }

        value = point.ToEngineering(raw);
        return true;
    }

    /// <summary>
    /// 写一个 AO 点：给的是工程值，按标定反算成原始码再下发。
    /// </summary>
    public bool WriteAo(int index, double engineering)
    {
        var point = Ao.Find(index);
        var plc = Plc;
        if (point is null || plc is null)
        {
            return false;
        }

        return plc.WriteAo(point.Index, point.ToRaw(engineering));
    }

    #endregion

    #region 开机下发 DO

    private List<(int Index, bool On)> _startupDo = new();
    private long _connectedAt;

    private bool _startupDoWritten;

    /// <summary>
    /// 每拍看一下 PLC：连上满 <see cref="StartupDoDelayMs"/> 就把开机 DO 写一遍。
    /// 本组件是一棵扫描树的根，由装配在 Open 之后 Start。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        WriteStartupDo();
        EnforceInterlock();
    }

    private void WriteStartupDo()
    {
        if (_startupDoWritten || _startupDo.Count == 0)
        {
            return;
        }

        var plc = Plc;
        if (plc is null || !plc.IsConnected)
        {
            // 等的过程中掉线了，重连后重新计时。
            _connectedAt = 0;
            return;
        }

        long now = Environment.TickCount64;
        if (_connectedAt == 0)
        {
            _connectedAt = now;
        }

        if (now - _connectedAt < StartupDoDelayMs)
        {
            return;
        }

        _startupDoWritten = true;
        var written = new List<string>();
        foreach (var (index, on) in _startupDo)
        {
            string text = $"DO{index}={(on ? "true" : "false")}";
            if (Do.Find(index) is null)
            {
                LogHelper.Warn($"[{FullPath}] 开机下发 {text}：DO 点表里没有这个点，没下发");
                continue;
            }

            if (!TryWriteDo(index, on, out var block))
            {
                LogHelper.Warn(block is null
                    ? $"[{FullPath}] 开机下发 {text}：写 PLC 失败"
                    : $"[{FullPath}] 开机下发 {text}：被安全联锁拒绝（{block.RuleText}）");
                continue;
            }

            written.Add(text);
        }

        LogHelper.Info($"[{FullPath}] 开机下发 DO {written.Count}/{_startupDo.Count} 个：{string.Join(";", written)}");
    }

    #endregion
}