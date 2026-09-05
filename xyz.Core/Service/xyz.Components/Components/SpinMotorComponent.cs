using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// 卡盘/旋钮（SpinMotor）轴组件：继承 AxisComponent，额外带取放片示教位 EC。
/// </summary>
[Component(description: "卡盘/旋钮轴组件")]
public class SpinMotorComponent : AxisComponent
{
    #region EC 可调参数

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit", "-100000", "100000", "0", "卡盘取放片示教位")]
    public double TransferPosition { get; set; }

    #endregion
}
