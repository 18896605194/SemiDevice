using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;

using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>
/// 摆臂组件：axis + 升降气缸+喷嘴
/// </summary>
[Component(description: "摆臂组件 (摆动轴；下面挂升降气缸和喷嘴)")]
public class SwingArmComponent : AxisComponent, ISwingArm
{
    #region EC 可调参数

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit", "-100000", "100000", "150", "Wafer 中心示教位：配方里晶圆中心（8 寸 100、12 寸 150）对应的实际轴位置，两种尺寸共用")]
    public double Center
    {
        get { return GetEcDouble(nameof(Center)); }
        set { SetEcDouble(nameof(Center), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit", "-100000", "100000", "50", "8 寸片边缘示教位：做 8 寸时配方 0（从 Home 摆过去先到的第一个边缘）对应的实际轴位置；没示教默认 50（12 寸边缘往里 50）")]
    public double Edge8
    {
        get { return GetEcDouble(nameof(Edge8)); }
        set { SetEcDouble(nameof(Edge8), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit", "-100000", "100000", "0", "12 寸片边缘示教位：做 12 寸时配方 0（从 Home 摆过去先到的第一个边缘）对应的实际轴位置")]
    public double Edge12
    {
        get { return GetEcDouble(nameof(Edge12)); }
        set { SetEcDouble(nameof(Edge12), value); }
    }

    /// <summary>
    /// 现在用的边缘示教位：按主界面切的晶圆尺寸取 <see cref="Edge8"/> 或 <see cref="Edge12"/>。
    /// </summary>
    public double Edge => EdgeOf(WaferSizeComponent.CurrentSize);

    #endregion

    #region 装在这条臂上的（sc.xml 里摆臂节点下面挂的）

    public IChamberLift? Lift
    {
        get
        {
            var lifts = Children.OfType<IChamberLift>().ToList();
            if (lifts.Count > 1)
            {
                throw new InvalidOperationException($"{FullPath}：摆臂下面只能装一个升降部件（{nameof(IChamberLift)}）");
            }

            return lifts.FirstOrDefault();
        }
    }

    public IReadOnlyList<INozzle> Nozzles => FindChildren<INozzle>();

    /// <summary>
    /// 初始化子组件
    /// </summary>
    /// <returns></returns>
    public override bool InitComponent()
    {
        _ = Lift;
        return base.InitComponent();
    }

    public INozzle? FindNozzle(string chemical)
    {
        return Nozzles.FirstOrDefault(nozzle => string.Equals(nozzle.Chemical, chemical, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region 配方坐标换算

    /// <summary>
    /// 晶圆坐标换成轴位置
    /// </summary>
    /// <param name="waferPosition"></param>
    /// <param name="nozzle"></param>
    /// <returns></returns>
    public double ToAxisPosition(double waferPosition, INozzle? nozzle = null)
    {
        int size = WaferSizeComponent.CurrentSize;
        double edge = EdgeOf(size);
        double offset = nozzle is null ? 0 : nozzle.Offset;
        return edge + (Center - edge) * waferPosition / WaferSizeComponent.RadiusOf(size) + offset;
    }

    private double EdgeOf(int waferSize)
    {
        return waferSize == WaferSizeComponent.Inch8 ? Edge8 : Edge12;
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
    /// 第一个边缘（当前尺寸的边缘示教位 <see cref="Edge"/>）在 Reach 上的位置 = Edge / Center：三维图据此分 Home → 边缘 → 中心两段画摆角。
    /// Edge 跟 Home 或 Center 分不开（12 寸还没示教时默认 Edge12 = 0，正好跟 Home 重合），或者不在两者之间，给 0——三维图就不分段。
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
