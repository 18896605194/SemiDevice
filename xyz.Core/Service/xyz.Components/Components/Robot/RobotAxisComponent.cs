using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// 机械手控制器轴：坐标由机械手驱动协议查询（不是 PLC 轴），只出数据、不动硬件、没有手动动作。
/// 节点名就是轴名；模块扫描查询回来后按轴名把坐标灌进 <see cref="Position"/>。
/// X / Z / Theta 直接用本类；手指（Arm）继承它，见 <see cref="RobotArmComponent"/>。
/// </summary>
[Component(description: "机械手控制器轴（只出数据，不接 PLC、无手动动作）")]
public class RobotAxisComponent : ComponentBase
{
    private readonly object _positionGate = new();
    private double? _position;

    /// <summary>
    /// 最近一次查询到的坐标；null = 还没查到。断线/停用时模块不下发这个值（可能是旧值），这里只存设备反馈。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Double,
        description: "机械手轴最近一次查询坐标（null=还没查到；断线时可能是旧值）")]
    [LiveValue]
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
    /// 记下查询回来的坐标。扫描线程调，只写缓存不做重活。
    /// </summary>
    internal void NotePosition(double position)
    {
        lock (_positionGate)
        {
            _position = position;
        }
    }
}
