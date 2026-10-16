using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>
/// Robot 轴除了arm
/// </summary>
[Component(description: "机械手控制器轴（只出数据，不接 PLC、无手动动作）")]
public class RobotAxisComponent : ComponentBase, IRobotAxis
{
    private readonly object _positionGate = new();
    private double? _position;

    /// <summary>
    /// 最近一次查询到的坐标；null = 还没查到。断线/停用时模块不下发这个值（可能是旧值），这里只存设备反馈。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Double,
        description: "机械手轴最近一次查询坐标（null=还没查到；断线时可能是旧值）")]
    public double? Position
    {
        get
        {
            lock (_positionGate)
            {
                return _position;
            }
        }
    }

    /// <summary>
    /// 记下查询回来的坐标（回包里没带坐标就是 null，算没查到）。扫描线程调，只写缓存不做重活。
    /// </summary>
    public void UpdatePosition(double? position)
    {
        lock (_positionGate)
        {
            _position = position;
        }
    }
}
