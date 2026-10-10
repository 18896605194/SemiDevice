using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Models;

namespace xyz.Components.Components;

/// <summary>
/// 机械手指（Robot 的 Arm）：本身就是一根轴（继承 <see cref="RobotAxisComponent"/>，节点名即轴名），
/// 额外带取放/推送用的手指号、传感器说的在位状态、晶圆账上这片片的只读视图。
/// 手指上的传感器（缩回到位、真空、边缘检测……）作为子组件挂在本节点下。
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

    private string? _ledgerModule;

    /// <summary>
    /// 传感器/控制器推送的物理在位；null = 还没收到该手指的推送（不是"没片"）。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "手指上是否有片（设备推送，null=还没收到）")]
    [LiveValue]
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
    /// 账上这片（只读视图）：位置是（模块名，槽位=手指号）。账的唯一权威是 WaferManagerComponent，
    /// 这里只查不存——人工调账、开机恢复、Job 都照旧改账，手臂跟着账走。
    /// </summary>
    public WaferInfo? Wafer
    {
        get
        {
            var ledger = WaferManagerComponent.Current;
            if (ledger is null || !ledger.IsEnable || _ledgerModule is null)
            {
                return null;
            }

            return ledger.Get(_ledgerModule, Number);
        }
    }

    /// <summary>
    /// 模块装配时把手指所在模块名告诉它（晶圆账位置用）。框架里组件没有父指针，只能由模块灌。
    /// </summary>
    internal void BindLedger(string module)
    {
        _ledgerModule = module;
    }

    /// <summary>
    /// 记下手指在位。驱动路由线程上调，只翻标志不做重活。
    /// </summary>
    internal void NoteWaferPresence(bool hasWafer)
    {
        lock (_waferGate)
        {
            _hasWafer = hasWafer ? 1 : 2;
        }
    }
}
