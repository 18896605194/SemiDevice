using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// 运动轴组件：先保留 EC 可调参数，SC 后续再加。
/// EC 全走 live 读写——现场改 ec.xml 立即生效，不经过这个对象的字段。
/// </summary>
[Component(description: "运动轴组件")]
public class AxisComponent : ComponentBase
{
    #region EC 可调参数

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s", "0.1", "10000", "10", "回零速度")]
    public double HomeSpeed
    {
        get { return GetEcDouble(nameof(HomeSpeed)); }
        set { SetEcDouble(nameof(HomeSpeed), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s", "0.1", "10000", "50", "默认移动速度")]
    public double MoveSpeed
    {
        get { return GetEcDouble(nameof(MoveSpeed)); }
        set { SetEcDouble(nameof(MoveSpeed), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s2", "0.1", "100000", "1000", "加速度")]
    public double Accel
    {
        get { return GetEcDouble(nameof(Accel)); }
        set { SetEcDouble(nameof(Accel), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s2", "0.1", "100000", "1000", "减速度")]
    public double Decel
    {
        get { return GetEcDouble(nameof(Decel)); }
        set { SetEcDouble(nameof(Decel), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s", "0.1", "10000", "100", "最大速度")]
    public double MaxSpeed
    {
        get { return GetEcDouble(nameof(MaxSpeed)); }
        set { SetEcDouble(nameof(MaxSpeed), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "1000", "300000", "30000", "运动等待超时")]
    public int TimeoutMs
    {
        get { return GetEcInt(nameof(TimeoutMs)); }
        set { SetEcInt(nameof(TimeoutMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "500", "60000", "5000", "停止等待超时")]
    public int StopTimeoutMs
    {
        get { return GetEcInt(nameof(StopTimeoutMs)); }
        set { SetEcInt(nameof(StopTimeoutMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "1000", "300000", "30000", "到速等待超时")]
    public int SpinTimeoutMs
    {
        get { return GetEcInt(nameof(SpinTimeoutMs)); }
        set { SetEcInt(nameof(SpinTimeoutMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit", "0.001", "100", "1.0", "到位容差")]
    public double PositionTolerance
    {
        get { return GetEcDouble(nameof(PositionTolerance)); }
        set { SetEcDouble(nameof(PositionTolerance), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s", "0.1", "10000", "5", "速度容差")]
    public double SpeedTolerance
    {
        get { return GetEcDouble(nameof(SpeedTolerance)); }
        set { SetEcDouble(nameof(SpeedTolerance), value); }
    }

    #endregion
}
