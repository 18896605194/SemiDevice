using xyz.Modules.Enums;

namespace xyz.Modules.StateMachines;

/// <summary>
/// LoadPort 动作与状态迁移规则表。
/// </summary>
public static class LoadPortStateTable
{
    public static IReadOnlyDictionary<
        (int? State, LoadPortAction Action),
        (int ExecutingState, int SuccessState)> Transitions { get; } =
        new Dictionary<(int? State, LoadPortAction Action), (int ExecutingState, int SuccessState)>
        {
            // (当前状态, 动作) = (执行状态, 成功状态)
            [(ModuleState.Idle, LoadPortAction.Load)] = (LoadPortState.Loading, LoadPortState.Loaded),
            [(LoadPortState.Loaded, LoadPortAction.Unload)] = (LoadPortState.Unloading, ModuleState.Idle),

            [(ModuleState.NotInit, LoadPortAction.Home)] = (LoadPortState.Homing, ModuleState.Idle),
            [(ModuleState.Idle, LoadPortAction.Home)] = (LoadPortState.Homing, ModuleState.Idle),
            [(ModuleState.Error, LoadPortAction.Home)] = (LoadPortState.Homing, ModuleState.Idle),
            [(LoadPortState.Loaded, LoadPortAction.Home)] = (LoadPortState.Homing, ModuleState.Idle),

            // Reset 不占用独立执行状态。
            [(ModuleState.NotInit, LoadPortAction.Reset)] = (ModuleState.NotInit, ModuleState.Idle),
            [(ModuleState.Idle, LoadPortAction.Reset)] = (ModuleState.Idle, ModuleState.Idle),
            [(ModuleState.Error, LoadPortAction.Reset)] = (ModuleState.Error, ModuleState.Idle),
            [(LoadPortState.Loaded, LoadPortAction.Reset)] = (LoadPortState.Loaded, ModuleState.Idle),

            // null 表示任意当前状态。
            [(null, LoadPortAction.Abort)] = (ModuleState.Aborting, ModuleState.Idle)
        };

    public static bool TryGetTransition(int state,LoadPortAction action, out (int ExecutingState, int SuccessState) transition)
    {
        return Transitions.TryGetValue((state, action), out transition)|| Transitions.TryGetValue((null, action), out transition);
    }
}
