using xyz.Elements.Enums;
using xyz.Modules.Enums;

namespace xyz.Modules.StateMachines;

/// <summary>
/// LoadPort 动作与状态迁移规则表。
/// </summary>
public static class LoadPortStateTable
{
    public static IReadOnlyDictionary<(int? State, string Action),(int ExecutingState, int SuccessState)> Transitions { get; } =
        new Dictionary<(int? State, string Action), (int ExecutingState, int SuccessState)>
        {
            // (当前状态, 动作) = (执行状态, 成功状态)
            [(ModuleState.Idle, LoadPortAction.Load)] = (LoadPortState.Loading, LoadPortState.Loaded),
            [(LoadPortState.Loaded, LoadPortAction.Unload)] = (LoadPortState.Unloading, ModuleState.Idle),

            // 单独扫一遍：门开着才扫，扫完还是 Loaded
            [(LoadPortState.Loaded, LoadPortAction.Map)] = (LoadPortState.Mapping, LoadPortState.Loaded),

            [(ModuleState.NotInit, LoadPortAction.Home)] = (LoadPortState.Homing, ModuleState.Idle),
            [(ModuleState.Idle, LoadPortAction.Home)] = (LoadPortState.Homing, ModuleState.Idle),
            [(ModuleState.Error, LoadPortAction.Home)] = (LoadPortState.Homing, ModuleState.Idle),
            [(LoadPortState.Loaded, LoadPortAction.Home)] = (LoadPortState.Homing, ModuleState.Idle),

         
            [(ModuleState.NotInit, LoadPortAction.Reset)] = (ModuleState.NotInit, ModuleState.NotInit),
            [(ModuleState.Idle, LoadPortAction.Reset)] = (ModuleState.Idle, ModuleState.Idle),
            [(ModuleState.Error, LoadPortAction.Reset)] = (ModuleState.Error, ModuleState.NotInit),
            [(LoadPortState.Loaded, LoadPortAction.Reset)] = (LoadPortState.Loaded, ModuleState.NotInit),

            [(ModuleState.Idle, LoadPortAction.Clamp)] = (LoadPortState.Clamping, ModuleState.Idle),
            [(ModuleState.Idle, LoadPortAction.Unclamp)] = (LoadPortState.Unclamping, ModuleState.Idle),


            [(ModuleState.Idle, LoadPortAction.Abort)] = (ModuleState.Aborting, ModuleState.Idle),
            [(ModuleState.Error, LoadPortAction.Abort)] = (ModuleState.Aborting, ModuleState.Error),


            [(null, LoadPortAction.Abort)] = (ModuleState.Aborting, ModuleState.NotInit)
        };
}
