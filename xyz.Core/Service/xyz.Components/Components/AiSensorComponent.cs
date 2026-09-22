using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// AI 值监控组件：上下限报警、预警带，超限/出预警带持续满 DurationMs 才报（报警防抖，EC）；可选监控使能 DO 门控。
/// </summary>
[Component(description: "AI 值监控组件")]
public class AiSensorComponent : ComponentBase
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

    #endregion

    #region EC 可调参数

    [VariableMark(VariableType.EC, ValueFormat.Double, null, null, null, "0", "报警下限（低于该值超限；上下限都为 0 表示未标定不判断）")]
    public double Min
    {
        get { return GetEcDouble(nameof(Min)); }
        set { SetEcDouble(nameof(Min), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, null, null, null, "0", "报警上限（高于该值超限；上下限都为 0 表示未标定不判断）")]
    public double Max
    {
        get { return GetEcDouble(nameof(Max)); }
        set { SetEcDouble(nameof(Max), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "0", "60000", "1000", "报警防抖：超限/出预警带持续满该时长才报")]
    public int DurationMs
    {
        get { return GetEcInt(nameof(DurationMs)); }
        set { SetEcInt(nameof(DurationMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, null, null, null, "0", "设定值（UI 展示/换算用）")]
    public double Setpoint
    {
        get { return GetEcDouble(nameof(Setpoint)); }
        set { SetEcDouble(nameof(Setpoint), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, null, null, null, "0", "预警下限（低于该值进入预警区；上下限都为 0 表示不判预警）")]
    public double WarningMin
    {
        get { return GetEcDouble(nameof(WarningMin)); }
        set { SetEcDouble(nameof(WarningMin), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, null, null, null, "0", "预警上限（高于该值进入预警区；上下限都为 0 表示不判预警）")]
    public double WarningMax
    {
        get { return GetEcDouble(nameof(WarningMax)); }
        set { SetEcDouble(nameof(WarningMax), value); }
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
        Solution = "检查对应工艺变量及传感器；上下限可在 EC Min/Max 调整")]
    public string AiSensorAlarm = nameof(AiSensorAlarm);

    #endregion

    #region 状态

    /// <summary>
    /// 最近一次读数；IO 没接或读失败为 null。
    /// </summary>
    public double? Value { get; private set; }

    /// <summary>
    /// 是否超限（防抖后）：超出 Min/Max 并持续满 DurationMs；回到范围内立刻算没超限。
    /// </summary>
    public bool IsOutOfRange { get; private set; }

    /// <summary>
    /// 是否在预警区（防抖后）：出了预警带并持续满 DurationMs，且没超限。
    /// </summary>
    public bool IsInWarning { get; private set; }

    #endregion

    #region 扫描

    /// <summary>
    /// 每个扫描周期读一次 AI，按防抖判超限、预警并报警。
    /// 监控没开（使能 DO 不在有效电平）时不判、防抖计时清零；读不到时不判，状态与防抖计时都不动。
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

        if (!IsMonitoring())
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
        IsOutOfRange = CheckAlarm(AiSensorAlarm, Outside(reading, Min, Max), duration, raise: AlarmEnabled);

        // 超限了就不再算预警：预警带比报警带窄，超限时一定也出了预警带。
        IsInWarning = CheckAlarm(AiSensorWarnAlarm,
            !IsOutOfRange && Outside(reading, WarningMin, WarningMax), duration, raise: AlarmEnabled);
    }

    /// <summary>
    /// 是否出了 [min, max]；上下限都为 0 表示没标定，不判。
    /// </summary>
    private static bool Outside(double value, double min, double max)
    {
        return (min != 0 || max != 0) && (value < min || value > max);
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
