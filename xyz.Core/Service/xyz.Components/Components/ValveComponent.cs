using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// 阀门组件：单 DO 驱动，可选到位反馈，反馈超时可通过 EC 调整。
/// </summary>
[Component(description: "阀门组件 (单 DO 驱动，可选到位反馈)")]
public class ValveComponent : ComponentBase
{
    #region SC 装机常量

    [SCEditor("-1", "IO", "阀门驱动 DO 索引", Required = true)]
    public int DoIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "开到位反馈 DI 索引 (-1 = 无反馈)")]
    public int DiOpenedIndex { get; set; } = -1;

    #endregion

    #region EC 可调参数

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "0", "60000", "3000", "反馈等待超时 (0 = 不等待反馈)")]
    public int FeedbackTimeoutMs { get; set; } = 3000;

    #endregion

    #region Alarm

    [Alarm("阀门到位反馈超时", AlarmCategory.Timeout,
        Description = "开/关阀后未在指定时间内收到到位反馈",
        Solution = "检查阀门气源、到位反馈点；无反馈阀可将 EC FeedbackTimeoutMs 设为 0")]
    public string FeedbackTimeoutAlarm = nameof(FeedbackTimeoutAlarm);

    #endregion
}
