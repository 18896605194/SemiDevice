using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// 单状态执行器底座：一个 DO 驱动 + 一个 DI 到位反馈。
/// 只有一个受控状态——通电到位，断电靠弹簧/自重/气压自己复位，复位那一侧不受控也没得反馈。
/// 阀门、单作用气缸这类都从它派生；DO/DI 的读写与到位判定写在这儿，派生类只加自己的语义属性。
/// </summary>
public abstract class OneStateComponent : ComponentBase
{
    #region SC 装机常量

    [SCEditor("-1", "IO", "驱动 DO 索引", Required = true)]
    public int DoIndex { get; set; } = -1;

    [SCEditor("-1", "IO", "到位 DI 索引 (-1 = 没接反馈点，不等反馈也不报超时)")]
    public int DiIndex { get; set; } = -1;

    #endregion

    #region EC 可调参数

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "100", "60000", "3000",
        "到位超时时间（没接 DI 时不生效）")]
    public int ActionTimeoutMs
    {
        get { return GetEcInt(nameof(ActionTimeoutMs)); }
        set { SetEcInt(nameof(ActionTimeoutMs), value); }
    }

    #endregion

    #region Alarm

    [Alarm("到位反馈超时", AlarmCategory.Timeout,
        Description = "驱动命令已发出，但在指定时间内未收到到位反馈",
        Solution = "检查气源压力、电磁阀及到位传感器；没接到位点就把 DI 索引配成 -1，"
                   + "不等也不报；超时时长在 EC ActionTimeoutMs 调")]
    public string TimeoutAlarm = nameof(TimeoutAlarm);

    #endregion
}
