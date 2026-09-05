using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// AI 值监控组件：支持监控门控、上下限报警、预警区，以及可调持续时间。
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
    public double Min { get; set; }

    [VariableMark(VariableType.EC, ValueFormat.Double, null, null, null, "0", "报警上限（高于该值超限；上下限都为 0 表示未标定不判断）")]
    public double Max { get; set; }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "0", "60000", "1000", "报警/预警持续时间（超限需持续该时长才触发）")]
    public int DurationMs { get; set; } = 1000;

    [VariableMark(VariableType.EC, ValueFormat.Double, null, null, null, "0", "设定值（UI 展示/换算用）")]
    public double Setpoint { get; set; }

    [VariableMark(VariableType.EC, ValueFormat.Double, null, null, null, "0", "预警下限（低于该值进入预警区）")]
    public double WarningMin { get; set; }

    [VariableMark(VariableType.EC, ValueFormat.Double, null, null, null, "0", "预警上限（高于该值进入预警区）")]
    public double WarningMax { get; set; }

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
}
