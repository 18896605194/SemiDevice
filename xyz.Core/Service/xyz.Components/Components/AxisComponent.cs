using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// 运动轴组件：先保留 EC 可调参数，SC 后续再加。
/// </summary>
[Component(description: "运动轴组件")]
public class AxisComponent : ComponentBase
{
    #region EC 可调参数

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s", "0.1", "10000", "10", "回零速度")]
    public double HomeSpeed { get; set; } = 10;

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s", "0.1", "10000", "50", "默认移动速度")]
    public double MoveSpeed { get; set; } = 50;

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s2", "0.1", "100000", "1000", "加速度")]
    public double Accel { get; set; } = 1000;

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s2", "0.1", "100000", "1000", "减速度")]
    public double Decel { get; set; } = 1000;

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s", "0.1", "10000", "100", "最大速度")]
    public double MaxSpeed { get; set; } = 100;

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "1000", "300000", "30000", "运动等待超时")]
    public int TimeoutMs { get; set; } = 30000;

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "500", "60000", "5000", "停止等待超时")]
    public int StopTimeoutMs { get; set; } = 5000;

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "1000", "300000", "30000", "到速等待超时")]
    public int SpinTimeoutMs { get; set; } = 30000;

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit", "0.001", "100", "1.0", "到位容差")]
    public double PositionTolerance { get; set; } = 1.0;

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s", "0.1", "10000", "5", "速度容差")]
    public double SpeedTolerance { get; set; } = 5;

    #endregion
}
