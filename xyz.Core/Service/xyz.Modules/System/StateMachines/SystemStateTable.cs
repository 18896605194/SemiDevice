using xyz.Components.Enums;
using xyz.Modules.Enums;

namespace xyz.Modules.StateMachines;

public static class SystemStateTable
{
    public static IReadOnlyDictionary<(int? State, SystemAction Action), (int ExecutingState, int SuccessState)> Transitions { get; } =
        new Dictionary<(int? State, SystemAction Action), (int ExecutingState, int SuccessState)>
        {
            // (当前状态, 动作) = (执行状态, 成功状态)
            // 全部初始化：做成了进空闲（Manual）
            [(ModuleState.NotInit, SystemAction.Init)] = (ModuleState.Initing, ModuleState.Idle),
            [(ModuleState.Idle, SystemAction.Init)] = (ModuleState.Initing, ModuleState.Idle),
            [(ModuleState.Error, SystemAction.Init)] = (ModuleState.Initing, ModuleState.Idle),

            // Auto / Manual 当场切，不挂动作；自动空闲、自动运行之间按有没有在跑的 Job 每拍自己切
            [(ModuleState.Idle, SystemAction.Auto)] = (SystemState.AutoIdle, SystemState.AutoIdle),
            [(SystemState.AutoIdle, SystemAction.Manual)] = (ModuleState.Idle, ModuleState.Idle),
            [(SystemState.AutoRunning, SystemAction.Manual)] = (ModuleState.Idle, ModuleState.Idle),

            // 全部回片只在 Manual 空闲时做
            [(ModuleState.Idle, SystemAction.ReturnAll)] = (SystemState.Returning, ModuleState.Idle),

            // 整机停止：停完回到停之前该在的地方——没初始化完的还是未初始化，自动、回片停下来回空闲（Manual）；停止中再按不收
            [(ModuleState.NotInit, SystemAction.Abort)] = (ModuleState.Aborting, ModuleState.NotInit),
            [(ModuleState.Initing, SystemAction.Abort)] = (ModuleState.Aborting, ModuleState.NotInit),
            [(ModuleState.Idle, SystemAction.Abort)] = (ModuleState.Aborting, ModuleState.Idle),
            [(SystemState.AutoIdle, SystemAction.Abort)] = (ModuleState.Aborting, ModuleState.Idle),
            [(SystemState.AutoRunning, SystemAction.Abort)] = (ModuleState.Aborting, ModuleState.Idle),
            [(SystemState.Returning, SystemAction.Abort)] = (ModuleState.Aborting, ModuleState.Idle),
            [(ModuleState.Error, SystemAction.Abort)] = (ModuleState.Aborting, ModuleState.Error),
        };

    public static IReadOnlyDictionary<(int? State, string Action), (int ExecutingState, int SuccessState)> ToModuleTable()
    {
        return Transitions.ToDictionary(kv => (kv.Key.State, kv.Key.Action.ToString()), kv => kv.Value);
    }
}
