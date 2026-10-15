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
public partial class IoComponent : ComponentBase
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

    [SCEditor("", "Io", "DI 触发时强制并锁定 DO，如 DI25=false:DO12=true,DO13=true;DI26=true:DO14=false；不分大小写，多条用分号、多个 DO 用逗号，支持中文标点；空 = 不启用")]
    public string SaftyIoInterlock { get; set; } = string.Empty;

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

    /// <summary>
    /// 装配校验：开机 DO 或联锁规则写错，启动时就报出来。
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
                    $"{Name}：StartupDo 里的“{item}”写得不对，要写成 DO12=false，多个用分号隔开");
            }

            startupDo.Add((index, on));
        }

        _startupDo = startupDo;
        LoadSaftyIoInterlock();
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
    /// 读点表；由装配在模块启动前调用。点表不在就是空表，照常空转不拦启动——
    /// 装机时电控还没给点表是常事；但已配置联锁时，所引用的点必须存在。
    /// </summary>
    public bool Open()
    {
        // 全限定：组件基类自己有个 Path 属性（组件路径），这儿的 Path 不是它。
        string directory = System.IO.Path.Combine(SC.ConfigDirectory, PointTableDirectory);

        Di.Load(System.IO.Path.Combine(directory, "DI.csv"));
        Do.Load(System.IO.Path.Combine(directory, "DO.csv"));
        Ai.Load(System.IO.Path.Combine(directory, "AI.csv"));
        Ao.Load(System.IO.Path.Combine(directory, "AO.csv"));
        ValidateInterlockPoints();

        LogHelper.Info($"[{FullPath}] 点表装载：DI {Di.Points.Count}、DO {Do.Points.Count}、"
                       + $"AI {Ai.Points.Count}、AO {Ao.Points.Count} 点（{directory}）");
        return true;
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
    /// 写一个 DO 点；关联 DI 触发时只允许配置的强制值，拒绝原因记日志。
    /// </summary>
    public bool WriteDo(int index, bool on)
    {
        lock (_doGate)
        {
            var point = Do.Find(index);
            var plc = Plc;
            if (point is null || plc is null)
            {
                return false;
            }

            var states = ReadInterlockStates();
            var blockers = _interlocks.Where(rule => rule.DoIndex == index && rule.DoValue != on
                && (states[rule.DiIndex] is null || states[rule.DiIndex] == rule.DiValue)).ToList();
            if (blockers.Count > 0)
            {
                string reason = $"DO{index} 无法下发 {BoolText(on)}：" + string.Join("；", blockers.Select(rule =>
                    states[rule.DiIndex] is null
                        ? $"DI{rule.DiIndex} 读不到，无法确认联锁已解除（规则：{rule.Text}）"
                        : $"DI{rule.DiIndex}={BoolText(rule.DiValue)} 联锁正在触发，DO{index} 被强制为 {BoolText(rule.DoValue)}（规则：{rule.Text}）"));
                LogHelper.Warn($"[{FullPath}] {reason}");
                EnforceInterlocks(states);
                return false;
            }

            return plc.WriteDo(point.Index, on);
        }
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
        lock (_doGate)
        {
            EnforceInterlocks(ReadInterlockStates());
        }
        WriteStartupDo();
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

            if (!WriteDo(index, on))
            {
                LogHelper.Warn($"[{FullPath}] 开机下发 {text}：写 PLC 失败或被 IO 联锁拒绝");
                continue;
            }

            written.Add(text);
        }

        LogHelper.Info($"[{FullPath}] 开机下发 DO {written.Count}/{_startupDo.Count} 个：{string.Join(";", written)}");
    }

    #endregion
}
