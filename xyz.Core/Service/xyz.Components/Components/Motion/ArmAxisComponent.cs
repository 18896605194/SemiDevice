using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// 摆臂轴组件：继承 AxisComponent，额外带 Wafer 示教位 EC。
/// 回零后轴在 0 位就是 Home；配方里的位置是晶圆坐标——从 Home 摆过去先碰到的第一个边缘是 0、晶圆中心是 150，
/// 按 Edge、Center 两个示教位（实际轴位置，比如 Edge = 100、Center = 200）线性换成轴位置。
/// 没示教时默认 Edge = 0、Center = 150，即轴位置就按晶圆坐标走。
/// 手动页跟别的轴一样一个页签；另外推 Reach / EdgeReach 给三维图画摆角。
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

    #region 三维图用的摆幅

    /// <summary>
    /// 摆到哪（三维图换成摆角）：0 = Home（回零后轴在 0 位），1 = 工艺位（EC Center，晶圆中心的示教位），
    /// 中间按轴位置线性换算，可以超出 0~1。Center 被改到跟 0 位分不开时没法换算，只分两档：在 0 位附近算 Home，离开了算工艺位。
    /// PLC 断了 CurrentPosition 停在断线前的值，这里也就停在原处；推的时候按 3 位小数取整，编码器抖一下不会一直推。
    /// </summary>
    [LiveValue]
    public double Reach
    {
        get
        {
            double position = CurrentPosition;
            double center = Center;
            double tolerance = PositionTolerance;
            if (Math.Abs(center) <= tolerance)
            {
                return Math.Abs(position) <= tolerance ? 0 : 1;
            }

            return position / center;
        }
    }

    /// <summary>
    /// 第一个边缘（EC Edge）在 Reach 上的位置 = Edge / Center：三维图据此分 Home → 边缘 → 中心两段画摆角。
    /// Edge 跟 Home 或 Center 分不开（还没示教时默认 Edge = 0，正好跟 Home 重合），或者不在两者之间，给 0——三维图就不分段。
    /// </summary>
    [LiveValue]
    public double EdgeReach
    {
        get
        {
            double center = Center;
            double edge = Edge;
            double tolerance = PositionTolerance;
            if (Math.Abs(center) <= tolerance || Math.Abs(edge) <= tolerance || Math.Abs(center - edge) <= tolerance)
            {
                return 0;
            }

            double reach = edge / center;
            return reach > 0 && reach < 1 ? reach : 0;
        }
    }

    #endregion
}
