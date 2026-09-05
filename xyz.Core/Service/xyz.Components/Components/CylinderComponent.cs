using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// 气缸组件：双 DO 驱动 + 双 DI 到位反馈，到位超时可通过 EC 调整。
/// </summary>
[Component(description: "气缸组件 (双 DO 驱动 + 双 DI 到位反馈)")]
public class CylinderComponent : ComponentBase
{
    #region SC 装机常量

    [SCEditor("-1", "IO", "开侧 DO 索引", Required = true)]
    public int DoOpenIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "关侧 DO 索引 (-1 = 单线圈)")]
    public int DoCloseIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "开到位 DI 索引", Required = true)]
    public int DiOpenedIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "关到位 DI 索引", Required = true)]
    public int DiClosedIndex { get; set; } = -1;

    #endregion

    #region EC 可调参数

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "100", "60000", "5000", "到位超时时间")]
    public int ActionTimeoutMs { get; set; } = 5000;

    #endregion

    #region Alarm

    [Alarm("开/关到位超时", AlarmCategory.Timeout,
        Description = "开/关命令已发出，但在指定时间内未收到到位反馈",
        Solution = "检查气源压力、电磁阀及到位传感器；可在 EC ActionTimeoutMs 调整超时")]
    public string TimeoutAlarm = nameof(TimeoutAlarm);

    #endregion
}
