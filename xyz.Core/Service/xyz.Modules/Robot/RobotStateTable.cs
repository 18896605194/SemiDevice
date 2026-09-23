using xyz.Modules.Enums;

namespace xyz.Modules.StateMachines;

/// <summary>
/// Robot 动作与状态迁移规则表。
/// </summary>
public static class RobotStateTable
{
    public static IReadOnlyDictionary<
        (int? State, RobotAction Action),
        (int ExecutingState, int SuccessState)> Transitions { get; } =
        new Dictionary<(int? State, RobotAction Action), (int ExecutingState, int SuccessState)>
        {
            // (当前状态, 动作) = (执行状态, 成功状态)
            [(ModuleState.NotInit, RobotAction.Home)] = (RobotState.Homing, ModuleState.Idle),
            [(ModuleState.Idle, RobotAction.Home)] = (RobotState.Homing, ModuleState.Idle),
            [(ModuleState.Error, RobotAction.Home)] = (RobotState.Homing, ModuleState.Idle),

            [(ModuleState.Idle, RobotAction.Pick)] = (RobotState.Picking, ModuleState.Idle),
            [(ModuleState.Idle, RobotAction.Place)] = (RobotState.Placing, ModuleState.Idle),

            // Reset 只清设备报错，不占用独立执行状态；报错后位置不可信，清错回 NotInit，需 Home 才回 Idle。
            [(ModuleState.NotInit, RobotAction.Reset)] = (ModuleState.NotInit, ModuleState.NotInit),
            [(ModuleState.Idle, RobotAction.Reset)] = (ModuleState.Idle, ModuleState.Idle),
            [(ModuleState.Error, RobotAction.Reset)] = (ModuleState.Error, ModuleState.NotInit),

            // 上/下使能不改变模块状态。
            [(ModuleState.NotInit, RobotAction.PowerOn)] = (ModuleState.NotInit, ModuleState.NotInit),
            [(ModuleState.Idle, RobotAction.PowerOn)] = (ModuleState.Idle, ModuleState.Idle),
            [(ModuleState.Error, RobotAction.PowerOn)] = (ModuleState.Error, ModuleState.Error),
            [(ModuleState.NotInit, RobotAction.PowerOff)] = (ModuleState.NotInit, ModuleState.NotInit),
            [(ModuleState.Idle, RobotAction.PowerOff)] = (ModuleState.Idle, ModuleState.Idle),
            [(ModuleState.Error, RobotAction.PowerOff)] = (ModuleState.Error, ModuleState.Error),

            // null 表示任意当前状态。急停可顶替在途动作；打断后位置不可信，回 NotInit 需重新 Home。
            [(null, RobotAction.Abort)] = (ModuleState.Aborting, ModuleState.NotInit)
        };

    /// <summary>
    /// 转成基类迁移表的注册键（动作名 = 枚举 ToString），供 Robot 模块实例注册自己的表；
    /// 机型可在返回值基础上增删后经 RegisterTransitions/AddTransition 定制。
    /// </summary>
    public static IReadOnlyDictionary<(int? State, string Action), (int ExecutingState, int SuccessState)> ToModuleTable()
    {
        return Transitions.ToDictionary(kv => (kv.Key.State, kv.Key.Action.ToString()),kv => kv.Value);
    }
}
