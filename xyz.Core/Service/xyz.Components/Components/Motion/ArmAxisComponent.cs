using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// 摆臂轴组件：继承 AxisComponent，额外带 Wafer 示教位 EC。
/// 回零后轴在 0 位就是 Home；配方里的位置是晶圆坐标——从 Home 摆过去先碰到的第一个边缘是 0、晶圆中心是 150，
/// 按 Edge、Center 两个示教位（实际轴位置，比如 Edge = 100、Center = 200）线性换成轴位置。
/// 没示教时默认 Edge = 0、Center = 150，即轴位置就按晶圆坐标走。
/// </summary>
[Component(description: "摆臂轴组件")]
public class ArmAxisComponent : AxisComponent
{
    #region EC 可调参数

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit", "-100000", "100000", "150", "Wafer 中心示教位：配方 150（晶圆中心）对应的实际轴位置")]
    public double Center
    {
        get { return GetEcDouble(nameof(Center)); }
        set { SetEcDouble(nameof(Center), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit", "-100000", "100000", "0", "Wafer 边缘示教位：配方 0（从 Home 摆过去先到的第一个边缘）对应的实际轴位置")]
    public double Edge
    {
        get { return GetEcDouble(nameof(Edge)); }
        set { SetEcDouble(nameof(Edge), value); }
    }

    #endregion
}
