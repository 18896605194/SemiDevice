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

    /// <summary>
    /// 转成基类迁移表的注册键（动作名 = 枚举 ToString），供 LoadPort 模块实例注册自己的表；
    /// 机型可在返回值基础上增删后经 RegisterTransitions/AddTransition 定制。
    /// </summary>
    public static IReadOnlyDictionary<(int? State, string Action), (int ExecutingState, int SuccessState)> ToModuleTable()
    {
        return Transitions.ToDictionary(
            kv => (kv.Key.State, kv.Key.Action.ToString()),
            kv => kv.Value);
    }
}
