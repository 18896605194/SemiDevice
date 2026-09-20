using xyz.Modules.Enums;

namespace xyz.Modules.StateMachines;

/// <summary>
/// 腔体动作与状态迁移规则表。
/// </summary>
public static class ChamberStateTable
{
    public static IReadOnlyDictionary<
        (int? State, ChamberAction Action),
        (int ExecutingState, int SuccessState)> Transitions { get; } =
        new Dictionary<(int? State, ChamberAction Action), (int ExecutingState, int SuccessState)>
        {
            // (当前状态, 动作) = (执行状态, 成功状态)
            [(ModuleState.NotInit, ChamberAction.Home)] = (ChamberState.Homing, ModuleState.Idle),
            [(ModuleState.Idle, ChamberAction.Home)] = (ChamberState.Homing, ModuleState.Idle),
            [(ModuleState.Error, ChamberAction.Home)] = (ChamberState.Homing, ModuleState.Idle),

            // 工艺只在空闲（门关、机械手不在里面）时起；片在不在由调用方查晶圆账。
            [(ModuleState.Idle, ChamberAction.Process)] = (ChamberState.Processing, ModuleState.Idle),

            // Reset 不占用独立执行状态。
            [(ModuleState.NotInit, ChamberAction.Reset)] = (ModuleState.NotInit, ModuleState.Idle),
            [(ModuleState.Idle, ChamberAction.Reset)] = (ModuleState.Idle, ModuleState.Idle),
            [(ModuleState.Error, ChamberAction.Reset)] = (ModuleState.Error, ModuleState.Idle),

            // 卡在交互环里也允许 Reset：取放片失败时站点会停在 Transferring 回不去，
            // 不给这几条路，人工就只剩 Abort 一条。落 Idle 等于强制脱离这一轮交互，
            // 片到底在手上还是在腔里得人工确认——所以这是人按的，自动流程发不出来。
            [(TransferModuleState.PreTransfer, ChamberAction.Reset)] = (TransferModuleState.PreTransfer, ModuleState.Idle),
            [(TransferModuleState.TransferReady, ChamberAction.Reset)] = (TransferModuleState.TransferReady, ModuleState.Idle),
            [(TransferModuleState.Transferring, ChamberAction.Reset)] = (TransferModuleState.Transferring, ModuleState.Idle),
            [(TransferModuleState.TransferComplete, ChamberAction.Reset)] = (TransferModuleState.TransferComplete, ModuleState.Idle),

            // null 表示任意当前状态。
            [(null, ChamberAction.Abort)] = (ModuleState.Aborting, ModuleState.Idle),
        };

    /// <summary>
    /// 转成基类迁移表的注册键（动作名 = 枚举 ToString），供腔体模块实例注册自己的表；
    /// 机型可在返回值基础上增删后经 RegisterTransitions/AddTransition 定制。
    /// </summary>
    public static IReadOnlyDictionary<(int? State, string Action), (int ExecutingState, int SuccessState)> ToModuleTable()
    {
        return Transitions.ToDictionary(kv => (kv.Key.State, kv.Key.Action.ToString()), kv => kv.Value);
    }
}
