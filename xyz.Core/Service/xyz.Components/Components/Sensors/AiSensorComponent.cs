using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>
/// AI 值监控组件：单个 AI，上下限 + 预警 + 防抖报警。别的组件、模块用它走 <see cref="IAiSensor"/>。
/// </summary>
[Component(description: "AI 值监控组件")]
public class AiSensorComponent : ComponentBase, IAiSensor
{
    #region SC 装机常量

    [SCEditor("-1", "IO", "监控的 AI 索引", Required = true)]
    public int AiIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "监控使能 DO 索引 (-1 = 始终监控)")]
    public int MonitoringDoIndex { get; set; } = -1;

    [SCEditor("True", "IO", "监控使能有效电平 (true = 高电平有效)")]
    public bool MonitoringActiveHigh { get; set; } = true;

    [SCEditor("True", "Config", "是否直接报警（false = 只提供状态，由宿主处理）")]
    public bool AlarmEnabled { get; set; } = true;

    [SCEditor("False", "Config", "上下限按目标值的百分比判（True：喷嘴流量计这类，工艺每步给目标值；False：上下限是固定数，压力、温度这类）")]
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

    [VariableMark(VariableType.EC, ValueFormat.Double, null, null, null, "0", "设定值（UI 展示/换算用）")]
    public double Setpoint
    {
        get { return GetEcDouble(nameof(Setpoint)); }
        set { SetEcDouble(nameof(Setpoint), value); }
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

    [Alarm("AI 测量值进入预警区间", AlarmCategory.ParameterControlError,
        AlarmLevel = AlarmLevel.Warn,
        Description = "AI 测量值进入预警区",
        Solution = "关注该工艺量趋势，必要时提前处理")]
    public string AiSensorWarnAlarm = nameof(AiSensorWarnAlarm);

    [Alarm("AI 测量值超出允许范围", AlarmCategory.ParameterControlError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "AI 测量值超出允许范围",
        Solution = "检查对应工艺变量及传感器；上下限在 EC AlarmMin/AlarmMax 调整（SC IsPercentOfTarget 为 True 时是目标值的百分比）")]
    public string AiSensorAlarm = nameof(AiSensorAlarm);

    #endregion

    #region 状态

    /// <summary>
    /// 最近一次读数；IO 没接或读失败为 null。
    /// </summary>
    public double? Value { get; private set; }

    /// <summary>
    /// 是否超限（防抖后）：超出 AlarmMin/AlarmMax（固定数，或按目标值的百分比）并持续满 DurationMs；回到范围内立刻算没超限。
    /// </summary>
    public bool IsOutOfRange { get; private set; }

    /// <summary>
    /// 是否在预警区（防抖后）：出了预警带并持续满 DurationMs，且没超限。
    /// </summary>
    public bool IsInWarning { get; private set; }

    private readonly object _gate = new();
    private double _target;

    /// <summary>
    /// 目标值：工艺给的（喷嘴按配方流量 SetFlow 时给它的流量计），IsPercentOfTarget 时上下限按它的百分比算；0 = 没给，不判。
    /// 运行时的值，不存盘——不用 EC 的 Setpoint，那个每步一改就要写 ec.xml、重启还留着上一步的值。
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
    /// 这一回开始监控的时刻（Environment.TickCount64）；没在监控为 -1。只在扫描线程里读写。
    /// </summary>
    private long _monitoringSince = -1;

    /// <summary>
    /// 上一拍看到的目标值，开着监控时目标变了要重新计延时。只在扫描线程里读写。
    /// </summary>
    private double _monitoringTarget;

    #endregion

    #region 目标值

    /// <summary>
    /// 给目标值（0 = 清掉，按百分比的就不判了）。开着监控时目标变了，开监控延时从变的那一拍重新计（换了流量要重新爬升）。
    /// </summary>
    public void SetTarget(double target)
    {
        lock (_gate)
        {
            _target = target;
        }
    }

    #endregion

    #region 扫描

    /// <summary>
    /// 每个扫描周期读一次 AI，按防抖判超限、预警并报警。
    /// 监控没开（使能 DO 不在有效电平）、或开了还没过 MonitoringDelayMs 时不判、防抖计时清零；读不到时不判，状态与防抖计时都不动。
    /// </summary>
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
    /// 是否出了上下限。固定数：出了 [min, max] 算，上下限都为 0 表示没标定、不判。
    /// 按目标值的百分比（IsPercentOfTarget）：高于 目标×(1+max/100) 或低于 目标×(1−min/100) 算，哪边是 0 哪边不判；
    /// 目标是 0（没给）不判——不在工艺里手动开阀时没有目标，按 0 判的话一开阀就报超上限。
    /// </summary>
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
    /// 读 AI 工程值；PLC 没连或点表里没这个索引返回 null。探针测试重写它直接摆读数。
    /// </summary>
    protected virtual double? ReadAi()
    {
        var io = IoComponent.Current;
        if (io is null || !io.TryReadAi(AiIndex, out double value))
        {
            return null;
        }

        return value;
    }

    /// <summary>
    /// 回读监控使能 DO（true = 高电平）；读不到返回 null（当作没开监控）。
    /// </summary>
    protected virtual bool? ReadMonitoringDo()
    {
        var io = IoComponent.Current;
        if (io is null || !io.TryReadDo(MonitoringDoIndex, out bool on))
        {
            return null;
        }

        return on;
    }

    #endregion
}
