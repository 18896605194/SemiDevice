using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// 摆臂轴组件：继承 AxisComponent，额外带 Wafer Center/Edge 标定 EC。
/// </summary>
[Component(description: "摆臂轴组件")]
public class ArmAxisComponent : AxisComponent
{
    #region EC 可调参数

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit", "-100000", "100000", "0", "Wafer 中心标定：recipe 0 对应的实际轴位置")]
    public double Center { get; set; }

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit", "-100000", "100000", "150", "Wafer 边缘标定：recipe 150 对应的实际轴位置")]
    public double Edge { get; set; } = 150;

    #endregion
}
