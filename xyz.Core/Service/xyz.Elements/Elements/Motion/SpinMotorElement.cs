using xyz.Elements;
using xyz.Elements.Attributes;
using xyz.Elements.Enums;

using xyz.Elements.Interfaces;

namespace xyz.Elements.Elements;

/// <summary>
/// SpinMotor
/// </summary>
[Element(description: "卡盘/旋钮轴元件")]
public class SpinMotorElement : AxisElement, ISpinMotor
{
    #region EC 可调参数

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit", "-100000", "100000", "0", "卡盘取放片示教位")]
    public double TransferPosition
    {
        get { return GetEcDouble(nameof(TransferPosition)); }
        set { SetEcDouble(nameof(TransferPosition), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "1000", "300000", "30000", "到速等待超时")]
    public int SpinTimeoutMs
    {
        get { return GetEcInt(nameof(SpinTimeoutMs)); }
        set { SetEcInt(nameof(SpinTimeoutMs), value); }
    }

    #endregion

    #region Alarm

    [Alarm("卡盘到速超时", AlarmCategory.ParameterControlError,
        Description = "转速命令已发出，但在指定时间内实际转速未进入目标转速容差",
        Solution = "检查驱动器使能、卡盘是否卡滞及 PLC 数据块通讯；超时时长在 EC SpinTimeoutMs、容差在 EC SpeedTolerance 调")]
    public string SpinTimeoutAlarm = nameof(SpinTimeoutAlarm);

    #endregion
}
