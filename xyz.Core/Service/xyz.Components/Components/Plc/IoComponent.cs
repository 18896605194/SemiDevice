using System.Globalization;
using xyz.Common;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Configs;
using xyz.Configs.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// IO 组件：点表 + 按索引读写。
/// </summary>
[Component(description: "IO 组件（点表 + 按索引读写）")]
public partial class IoComponent : ComponentBase, IIo
{
    public IoComponent()
    {
        // 开机准备排在 PLC 后面、别的组件前面：DI、AI 组件要按点表认点
        InitOrder = 200;
    }

    /// <summary>
    /// 开机准备：取 PLC、读点表（别的组件开机准备时就要经它读写，比如 E84 开机清输出）
    /// </summary>
    public override bool InitComponent()
    {
        _plc = IocHelper.GetService<IPlcIo>();
        Open();
        return base.InitComponent();
    }

    /// <summary>
    /// 本组件用的 PLC（开机准备时取）；machine.xml 没配 PLC 是 null
    /// </summary>
    private IPlcIo? _plc;

    private VirtualInputRegistry? _virtualInputs;

    internal void AttachVirtualInputs(VirtualInputRegistry registry) => _virtualInputs = registry;

    void IIo.AttachVirtualInputs(VirtualInputRegistry registry)
    {
        AttachVirtualInputs(registry);
    }

    #region Machine

    [Machine("IO", "Io", "点表目录（相对 machine.xml 所在目录），里面放 DI.csv / DO.csv / AI.csv / AO.csv")]
    public string PointTableDirectory { get; set; } = "IO";

    [Machine("", "Io", "开机下发的 DO：连上 PLC 后按这里写一遍，如 DO12=false;DO13=true，多个用分号隔开，DO 不分大小写；空 = 不下发")]
    public string StartupDo { get; set; } = string.Empty;

    [Machine("", "Io", "DI 触发时强制并锁定 DO，如 DI25=false:DO12=true,DO13=true;DI26=true:DO14=false；不分大小写，多条用分号、多个 DO 用逗号，支持中文标点；空 = 不启用")]
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
    /// 物理采集是否在工作（SV）：PLC 连着才算。虚拟输入按其来源单独判断有效性，纯常量不依赖 PLC。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "IO 采集是否在工作")]
    public bool IsCollected => _plc?.IsConnected ?? false;

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
        string directory = System.IO.Path.IsPathRooted(PointTableDirectory)
            ? PointTableDirectory
            : System.IO.Path.Combine(MachineXml.ConfigDirectory, PointTableDirectory);

        Di.Load(System.IO.Path.Combine(directory, "DI.csv"), this);
        Do.Load(System.IO.Path.Combine(directory, "DO.csv"), this);
        Ai.Load(System.IO.Path.Combine(directory, "AI.csv"), this);
        Ao.Load(System.IO.Path.Combine(directory, "AO.csv"), this);
        _virtualInputs?.ValidateSources();
        ValidateInterlockPoints();

        LogHelper.Info(this, $"点表装载：DI {Di.Points.Count}、DO {Do.Points.Count}、"
                       + $"AI {Ai.Points.Count}、AO {Ao.Points.Count} 点（{directory}）");
        return true;
    }

    #endregion

    #region 按索引读写（物理点与虚拟输入；未配置或数据无效时返回 false）

    /// <summary>
    /// 读一个 DI 点。
    /// </summary>
    public bool TryReadDi(int index, out bool on)
    {
        on = false;
        if (index >= 3000)
        {
            double value = 0;
            bool valid = index is >= 4000 and < 5000
                && (_virtualInputs?.TryRead(new IoReference("DI", index), out value) ?? false);
            on = valid && value != 0;
            return valid;
        }

        var point = Di.Find(index);
        if (point is null || _plc is null)
        {
            return false;
        }

        return _plc.TryReadDi(point.Index, out on);
    }

    /// <summary>
    /// 回读一个 DO 点当前的输出状态。
    /// </summary>
    public bool TryReadDo(int index, out bool on)
    {
        on = false;
        if (index >= 3000)
        {
            return false;
        }

        var point = Do.Find(index);
        if (point is null || _plc is null)
        {
            return false;
        }

        return _plc.TryReadDo(point.Index, out on);
    }

    /// <summary>
    /// 写一个 DO 点；关联 DI 触发时只允许配置的强制值，被联锁拦下带原因（命中的规则另记日志）
    /// </summary>
    public HandleResult WriteDo(int index, bool on)
    {
        // 虚拟点只保留计算型 DI/AI；预留的输出区间不能下发到 PLC。
        if (index >= 3000)
        {
            return WriteFailed("DO", index);
        }

        lock (_doGate)
        {
            var point = Do.Find(index);
            if (point is null || _plc is null)
            {
                return WriteFailed("DO", index);
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
                LogHelper.Warn(this, $"{reason}");
                EnforceInterlocks(states);
                return InterlockRejected(index, on, blockers, states);
            }

            if (!_plc.WriteDo(point.Index, on))
            {
                return WriteFailed("DO", index);
            }

            return HandleResult.Success();
        }
    }

    /// <summary>
    /// 读一个 AI 点的工程值（按点表里的标定换算；没标定就是原始码）。
    /// </summary>
    public bool TryReadAi(int index, out double value)
    {
        value = 0;
        if (index >= 3000)
        {
            return index is >= 6000 and < 7000
                && (_virtualInputs?.TryRead(new IoReference("AI", index), out value) ?? false);
        }

        var point = Ai.Find(index);
        if (point is null || _plc is null || !_plc.TryReadAi(point.Index, out double raw))
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
        if (index >= 3000)
        {
            return false;
        }

        var point = Ao.Find(index);
        if (point is null || _plc is null || !_plc.TryReadAo(point.Index, out double raw))
        {
            return false;
        }

        value = point.ToEngineering(raw);
        return true;
    }

    /// <summary>
    /// 写一个 AO 点：给的是工程值，标定过的点先按工程量范围卡住，再按标定反算成原始码下发
    /// </summary>
    public HandleResult WriteAo(int index, double engineering)
    {
        if (index >= 3000)
        {
            return WriteFailed("AO", index);
        }

        var point = Ao.Find(index);
        if (point is null || _plc is null)
        {
            return WriteFailed("AO", index);
        }

        // 模拟量输出多半是流量、压力、转速设定，给过头了设备会照做；谁写都一样卡（手动页、喷嘴流量）
        if (point.IsScaled)
        {
            double min = Math.Min(point.LogicalMin, point.LogicalMax);
            double max = Math.Max(point.LogicalMin, point.LogicalMax);
            if (engineering < min || engineering > max)
            {
                return HandleResult.Fail(ErrorCodes.IoOutOfRange, index.ToString(CultureInfo.InvariantCulture),
                    ValueHelper.ToText(min), ValueHelper.ToText(max), point.Unit);
            }
        }

        if (!_plc.WriteAo(point.Index, point.ToRaw(engineering)))
        {
            return WriteFailed("AO", index);
        }

        return HandleResult.Success();
    }

    /// <summary>
    /// 写不成（io.write_failed）：PLC 没装、点表里没有、预留区间、写 PLC 出错
    /// </summary>
    private static HandleResult WriteFailed(string type, int index)
    {
        return HandleResult.Fail(ErrorCodes.IoWriteFailed, type, index.ToString(CultureInfo.InvariantCulture));
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
        PublishSnapshot();
    }

    private void WriteStartupDo()
    {
        if (_startupDoWritten || _startupDo.Count == 0)
        {
            return;
        }

        if (_plc is null || !_plc.IsConnected)
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
                LogHelper.Warn(this, $"开机下发 {text}：DO 点表里没有这个点，没下发");
                continue;
            }

            var result = WriteDo(index, on);
            if (!result.IsSuccess)
            {
                LogHelper.Warn(this, $"开机下发 {text} 没下发：{result.ErrorMessage} [{string.Join(", ", result.Args)}]");
                continue;
            }

            written.Add(text);
        }

        LogHelper.Info(this, $"开机下发 DO {written.Count}/{_startupDo.Count} 个：{string.Join(";", written)}");
    }

    #endregion
}
