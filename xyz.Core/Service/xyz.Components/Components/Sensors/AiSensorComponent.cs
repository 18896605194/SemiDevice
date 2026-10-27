using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// AI 值监控组件
/// </summary>
[Component(description: "AI 值监控组件")]
public class AiSensorComponent : ComponentBase, IAiSensor
{
    #region Machine 装机常量

    [Machine("-1", "IO", "监控的 AI 索引", Required = true)]
    public int AiIndex { get; set; } = -1;

    [Machine("-1", "IO", "监控使能 DO 索引 (-1 = 始终监控)")]
    public int MonitoringDoIndex { get; set; } = -1;

    [Machine("True", "IO", "监控使能有效电平 (true = 高电平有效)")]
    public bool MonitoringActiveHigh { get; set; } = true;

    [Machine("True", "Config", "是否直接报警（false = 只提供状态，由宿主处理）")]
    public bool AlarmEnabled { get; set; } = true;

    [Machine("False", "Config", "上下限按目标值的百分比判（True：喷嘴流量计这类，工艺每步给目标值；False：上下限是固定数，压力、温度这类）")]
    public bool IsPercentOfTarget { get; set; }

    #endregion

    #region EC 可调参数

    [VariableMark(VariableType.EC, ValueFormat.Double, null, null, null, "0", "报警下限：固定数时低于它超限（上下限都为 0 表示未标定不判断）；按目标百分比时低于 目标×(1−它/100) 超限、0 = 下边不判")]
    public double AlarmMin
    {
        get { return GetEcDouble(nameof(AlarmMin)); }
        set { SetEcDouble(nameof(AlarmMin), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, null, null, null, "0", "报警上限：固定数时高于它超限（上下限都为 0 表示未标定不判断）；按目标百分比时高于 目标×(1+它/100) 超限、0 = 上边不判")]
    public double AlarmMax
    {
        get { return GetEcDouble(nameof(AlarmMax)); }
        set { SetEcDouble(nameof(AlarmMax), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "0", "60000", "1000", "报警防抖：超限/出预警带持续满该时长才报")]
    public int DurationMs
    {
        get { return GetEcInt(nameof(DurationMs)); }
        set { SetEcInt(nameof(DurationMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "0", "600000", "0", "开监控后延时：监控使能 DO 变有效后等这么久才开始判（比如开阀后流量还在爬升）；没配 DO 的从开机算；0 = 马上判")]
    public int MonitoringDelayMs
    {
        get { return GetEcInt(nameof(MonitoringDelayMs)); }
        set { SetEcInt(nameof(MonitoringDelayMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, null, null, null, "0", "预警下限：固定数时低于它进入预警区（上下限都为 0 表示不判预警）；按目标百分比时低于 目标×(1−它/100) 进入预警区、0 = 下边不判")]
    public double WarnMin
    {
        get { return GetEcDouble(nameof(WarnMin)); }
        set { SetEcDouble(nameof(WarnMin), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, null, null, null, "0", "预警上限：固定数时高于它进入预警区（上下限都为 0 表示不判预警）；按目标百分比时高于 目标×(1+它/100) 进入预警区、0 = 上边不判")]
    public double WarnMax
    {
        get { return GetEcDouble(nameof(WarnMax)); }
        set { SetEcDouble(nameof(WarnMax), value); }
    }

    #endregion

    #region Alarm

    [Alarm("AI 测量值进入预警区间", AlarmCategory.ParameterControlWarning,
        AlarmLevel = AlarmLevel.Warn,
        Description = "AI 测量值进入预警区",
        Solution = "关注该工艺量趋势，必要时提前处理")]
    public string AiSensorWarnAlarm = nameof(AiSensorWarnAlarm);

    [Alarm("AI 测量值超出允许范围", AlarmCategory.ParameterControlError,
        AlarmLevel = AlarmLevel.Light,
        Description = "AI 测量值超出允许范围",
        Solution = "检查对应工艺变量及传感器；上下限在 EC AlarmMin/AlarmMax 调整（machine.xml IsPercentOfTarget 为 True 时是目标值的百分比）")]
    public string AiSensorAlarm = nameof(AiSensorAlarm);

    #endregion

    #region 状态

    /// <summary>
    /// 最近一次读数
    /// </summary>
    public double? Value { get; private set; }

    /// <summary>
    /// 是否超限（防抖后）
    /// </summary>
    public bool IsOutOfRange { get; private set; }

    /// <summary>
    /// 是否在预警区（防抖后）
    /// </summary>
    public bool IsInWarning { get; private set; }

    private readonly object _gate = new();
    private double _target;

    /// <summary>
    /// 目标值 其实就是下发
    /// </summary>
    public double Target
    {
        get
        {
            lock (_gate)
            {
                return _target;
            }
        }
    }

    /// <summary>
    /// 开始监控的时刻
    /// </summary>
    private long _monitoringSince = -1;

    /// <summary>
    /// 上一拍看到的目标值
    /// </summary>
    private double _monitoringTarget;

    #endregion

    #region 目标值

    /// <summary>
    /// 设置目标值
    /// </summary>
    public void SetTarget(double target)
    {
        lock (_gate)
        {
            _target = target;
        }
    }

    #endregion

    /// <summary>
    /// IO 组件（开机 Bind 时取）；machine.xml 没配 Io 节点是 null
    /// </summary>
    private IIo? _io;

    public override void Bind()
    {
        _io = IocHelper.GetService<IIo>();
    }

    #region 扫描

   
    protected override void OnScan()
    {
        base.OnScan();
        if (AiIndex < 0)
        {
            return;
        }

        var value = ReadAi();
        Value = value;

        bool monitoring = IsMonitoring();
        double target = Target;
        long now = Environment.TickCount64;
        if (!monitoring)
        {
            _monitoringSince = -1;
        }
        else if (_monitoringSince < 0 || target != _monitoringTarget)
        {
            // 刚开监控，或者开着换了目标值（同一个喷嘴连着两步、流量不一样）：延时从现在重新计
            _monitoringSince = now;
        }

        _monitoringTarget = target;

        // 没开监控，或者开了还没过延时（开阀后流量还在爬升）：不判，防抖计时清零
        if (!monitoring || now - _monitoringSince < MonitoringDelayMs)
        {
            IsOutOfRange = CheckAlarm(AiSensorAlarm, false, 0);
            IsInWarning = CheckAlarm(AiSensorWarnAlarm, false, 0);
            return;
        }

        if (value is null)
        {
            return;
        }

        double reading = value.Value;
        int duration = DurationMs;
        IsOutOfRange = CheckAlarm(AiSensorAlarm, Outside(reading, target, AlarmMin, AlarmMax), duration, raise: AlarmEnabled);

        // 超限了就不再算预警：预警带比报警带窄，超限时一定也出了预警带。
        IsInWarning = CheckAlarm(AiSensorWarnAlarm,
            !IsOutOfRange && Outside(reading, target, WarnMin, WarnMax), duration, raise: AlarmEnabled);
    }

    /// <summary>
    /// 是否出了上下限  值和百分比共存
    /// </summary>
    /// <param name="value"></param>
    /// <param name="target"></param>
    /// <param name="min"></param>
    /// <param name="max"></param>
    /// <returns></returns>
    private bool Outside(double value, double target, double min, double max)
    {
        if (!IsPercentOfTarget)
        {
            return (min != 0 || max != 0) && (value < min || value > max);
        }

        if (target == 0)
        {
            return false;
        }

        return (max > 0 && value > target * (1 + max / 100))
            || (min > 0 && value < target * (1 - min / 100));
    }

    private bool IsMonitoring()
    {
        if (MonitoringDoIndex < 0)
        {
            return true;
        }

        var level = ReadMonitoringDo();
        return level is not null && level.Value == MonitoringActiveHigh;
    }

    /// <summary>
    /// 读取 AI 
    /// </summary>
    protected virtual double? ReadAi()
    {
        if (_io is null || !_io.TryReadAi(AiIndex, out double value))
        {
            return null;
        }

        return value;
    }

    /// <summary>
    /// 回读监控使能 DO
    /// </summary>
    protected virtual bool? ReadMonitoringDo()
    {
        if (_io is null || !_io.TryReadDo(MonitoringDoIndex, out bool on))
        {
            return null;
        }

        return on;
    }

    #endregion
}
