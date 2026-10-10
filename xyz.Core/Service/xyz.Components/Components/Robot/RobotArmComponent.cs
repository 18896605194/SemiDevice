using xyz.Components.Attributes;
using xyz.Components.Enums;

namespace xyz.Components.Components;

/// <summary>
/// 机械手指（Robot 的 Arm）：本身就是一根轴（继承 <see cref="RobotAxisComponent"/>，节点名即轴名），
/// 额外带取放/推送用的手指号、传感器/控制器推送的在位状态。
/// 手指上的传感器（缩回到位、真空、边缘检测）作为子组件挂在本节点下。
/// </summary>
[Component(description: "机械手指（Robot 的 Arm，本身是一根 Arm 轴）")]
public class RobotArmComponent : RobotAxisComponent
{
    #region SC

    [SCEditor("1", "Arm", "手指号（取放与主动推送协议用，从 1 开始）", Required = true)]
    public int Number { get; set; } = 1;

    #endregion

    private readonly object _waferGate = new();

    /// <summary>0 = 还没收到推送，1 = 有片，2 = 无片。</summary>
    private int _hasWafer;

    /// <summary>
    /// 传感器/控制器推送的物理在位；null = 还没收到该手指的推送（不是"没片"）。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "手指上是否有片（设备推送，null=还没收到）")]
    public bool? HasWafer
    {
        get
        {
            lock (_waferGate)
            {
                return _hasWafer switch
                {
                    1 => true,
                    2 => false,
                    _ => null,
                };
            }
        }
    }

    /// <summary>
    /// 记下手指在位。驱动路由线程上调，只翻标志不做重活。
    /// </summary>
    internal void UpdateWaferPresence(bool hasWafer)
    {
        lock (_waferGate)
        {
            _hasWafer = hasWafer ? 1 : 2;
        }
    }
}