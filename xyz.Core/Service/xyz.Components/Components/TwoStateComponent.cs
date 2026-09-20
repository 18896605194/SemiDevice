using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// 双状态执行器底座：两个 DO 各驱一个方向 + 两个 DI 各给一侧到位反馈。
/// 两个方向都受控，断电保持在原位。双作用气缸这类都从它派生；
/// DO/DI 的读写与到位判定写在这儿，派生类只加自己的语义属性。
/// 两侧反馈各自可选：哪一侧 DI 配成 -1，哪一侧就不等反馈，也不会因此报超时。
/// </summary>
public abstract class TwoStateComponent : ComponentBase
{
    #region SC 装机常量

    [SCEditor("-1", "IO", "开侧驱动 DO 索引", Required = true)]
    public int DoOpenIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "关侧驱动 DO 索引", Required = true)]
    public int DoCloseIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "开到位 DI 索引 (-1 = 没接反馈点，不等反馈也不报超时)")]
    public int DiOpenedIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "关到位 DI 索引 (-1 = 没接反馈点，不等反馈也不报超时)")]
    public int DiClosedIndex { get; set; } = -1;

    #endregion

    #region EC 可调参数

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "100", "60000", "5000",
        "到位超时时间（只管接了 DI 的那一侧）")]
    public int ActionTimeoutMs
    {
        get { return GetEcInt(nameof(ActionTimeoutMs)); }
        set { SetEcInt(nameof(ActionTimeoutMs), value); }
    }

    #endregion

    #region Alarm

    [Alarm("开/关到位超时", AlarmCategory.Timeout,
        Description = "开/关命令已发出，但在指定时间内未收到该侧到位反馈",
        Solution = "检查气源压力、电磁阀及到位传感器；没接到位点的那一侧把 DI 索引配成 -1，"
                   + "不等也不报；超时时长在 EC ActionTimeoutMs 调")]
    public string TimeoutAlarm = nameof(TimeoutAlarm);

    #endregion
}
