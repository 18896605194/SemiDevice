using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// 摆臂组件：腔体里的一条摆臂。它自己就是摆动轴（继承 AxisComponent，走 PLC 数据块）；
/// 硬件上跟它装在一起的升降、喷嘴在 sc.xml 里挂在它下面——升降是一个气缸组件（第一个气缸），喷嘴是几个喷嘴组件（这条臂上有哪些药液）。
/// 回零后轴在 0 位就是 Home；配方里的位置是晶圆坐标——从 Home 摆过去先碰到的第一个边缘是 0、晶圆中心是 150，
/// 按 Edge、Center 两个示教位（实际轴位置，比如 Edge = 100、Center = 200）线性换成轴位置。
/// 没示教时默认 Edge = 0、Center = 150，即轴位置就按晶圆坐标走。
/// 手动页跟别的轴一样一个页签；另外推 Reach / EdgeReach 给三维图画摆角。
/// </summary>
[Component(description: "摆臂组件 (摆动轴；下面挂升降气缸和喷嘴)")]
public class SwingArmComponent : AxisComponent
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

    #region 装在这条臂上的（sc.xml 里摆臂节点下面挂的）

    /// <summary>这条臂的升降气缸：摆臂节点下第一个气缸；sc 里没配为 null。</summary>
    public TwoStateComponent? Lift => FindChild<TwoStateComponent>();

    /// <summary>这条臂上的喷嘴，按 sc 的先后。</summary>
    public IReadOnlyList<NozzleComponent> Nozzles => FindChildren<NozzleComponent>();

    /// <summary>按药液名找这条臂上的喷嘴（照 sc 的 Chemical，忽略大小写）；没有为 null。</summary>
    public NozzleComponent? FindNozzle(string chemical)
    {
        return Nozzles.FirstOrDefault(nozzle => string.Equals(nozzle.Chemical, chemical, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region 配方坐标换算（腔体按工艺配方摆臂时用）

    /// <summary>晶圆坐标里中心的位置：配方的位置 0 = 第一个边缘，150 = 中心。</summary>
    private const double WaferCenter = 150;

    /// <summary>配方里的晶圆坐标换成轴位置：按 Edge、Center 两个示教位线性换算（Edge = 0 位、Center = 150 位）。</summary>
    public double ToAxisPosition(double waferPosition)
    {
        double edge = Edge;
        return edge + (Center - edge) * waferPosition / WaferCenter;
    }

    /// <summary>配方里的扫描速度（晶圆坐标每秒）换成轴速度，比例跟位置一样；Edge、Center 一样（示教错了）时是 0，轴不会接这个指令。</summary>
    public double ToAxisSpeed(double waferSpeed)
    {
        return Math.Abs(Center - Edge) * waferSpeed / WaferCenter;
    }

    #endregion

    #region 三维图用的摆幅

    /// <summary>
    /// 摆到哪（三维图换成摆角）：0 = Home（回零后轴在 0 位），1 = 工艺位（EC Center，晶圆中心的示教位），
    /// 中间按轴位置线性换算，可以超出 0~1。Center 被改到跟 0 位分不开时没法换算，只分两档：在 0 位附近算 Home，离开了算工艺位。
    /// PLC 断了 CurrentPosition 停在断线前的值，这里也就停在原处；推的时候按 3 位小数取整，编码器抖一下不会一直推。
    /// </summary>
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
