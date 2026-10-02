using xyz.Components;
using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// 卡盘/旋钮（SpinMotor）轴组件：继承 AxisComponent，额外带取放片示教位和到速超时 EC。
/// 到速超时只有卡盘这种连续旋转的轴才有，定位轴（摆臂）用不上，所以不放基类。
/// </summary>
[Component(description: "卡盘/旋钮轴组件")]
public class SpinMotorComponent : AxisComponent
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

    [Alarm("卡盘到速超时", AlarmCategory.Timeout,
        Description = "转速命令已发出，但在指定时间内实际转速未进入目标转速容差",
        Solution = "检查驱动器使能、卡盘是否卡滞及 PLC 数据块通讯；超时时长在 EC SpinTimeoutMs、容差在 EC SpeedTolerance 调")]
    public string SpinTimeoutAlarm = nameof(SpinTimeoutAlarm);

    #endregion
}
