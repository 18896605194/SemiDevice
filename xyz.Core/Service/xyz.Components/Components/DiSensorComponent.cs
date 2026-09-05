using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// DI 值监控组件：单个 DI，支持电平触发、防抖和报警声明。
/// </summary>
[Component(description: "DI 值监控组件")]
public class DiSensorComponent : ComponentBase
{
    #region SC 装机常量

    [SCEditor("-1", "IO", "监控的 DI 索引", Required = true)]
    public int DiIndex { get; set; } = -1;

    [SCEditor("High", "Config", "触发有效电平：High=高电平报警，Low=低电平报警")]
    public TriggerLevel TriggerLevel { get; set; } = TriggerLevel.High;

    [SCEditor("True", "Config", "是否直接报警（false = 只提供触发状态，由宿主处理）")]
    public bool AlarmEnabled { get; set; } = true;

    #endregion

    #region EC 可调参数

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "0", "10000", "200", "防抖时间：电平需稳定该时长才被识别")]
    public int DebounceMs { get; set; } = 200;

    #endregion

    #region Alarm

    [Alarm("开关信号异常", AlarmCategory.SensorError,
        Description = "DI 数字输入处于配置的报警电平",
        Solution = "检查对应设备/互锁状态及该 DI 点接线")]
    public string SensorAlarm = nameof(SensorAlarm);

    #endregion
}
