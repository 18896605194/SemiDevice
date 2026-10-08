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

            // Reset 只清错、不动机构，不占用独立执行状态。没初始化、出过错的复位完还是没初始化，要再 Home 一次（照老 CTC）；
            // Loaded 复位完先落 Idle，门其实开着的由模块按状态查询改回 Loaded（BaseLoadPortModule.OnOperationCompleted）。
            [(ModuleState.NotInit, LoadPortAction.Reset)] = (ModuleState.NotInit, ModuleState.NotInit),
            [(ModuleState.Idle, LoadPortAction.Reset)] = (ModuleState.Idle, ModuleState.Idle),
            [(ModuleState.Error, LoadPortAction.Reset)] = (ModuleState.Error, ModuleState.NotInit),
            [(LoadPortState.Loaded, LoadPortAction.Reset)] = (LoadPortState.Loaded, ModuleState.Idle),

            // Clamp/Unclamp 只在空闲（门关）时允许；Loaded（门开）不允许松开。
            [(ModuleState.Idle, LoadPortAction.Clamp)] = (LoadPortState.Clamping, ModuleState.Idle),
            [(ModuleState.Idle, LoadPortAction.Unclamp)] = (LoadPortState.Unclamping, ModuleState.Idle),

            // Abort 只停、能顶替任何在途动作。没初始化、Home 被打断的中止完还是没初始化；出错的中止完还是出错——
            // 不然点一下中止就绕过了复位和 Home。
            [(ModuleState.NotInit, LoadPortAction.Abort)] = (ModuleState.Aborting, ModuleState.NotInit),
            [(LoadPortState.Homing, LoadPortAction.Abort)] = (ModuleState.Aborting, ModuleState.NotInit),
            [(ModuleState.Error, LoadPortAction.Abort)] = (ModuleState.Aborting, ModuleState.Error),

            // 其余（null 表示任意当前状态）先落 Idle，门开着、没在动的由模块按状态查询改回 Loaded。
            [(null, LoadPortAction.Abort)] = (ModuleState.Aborting, ModuleState.Idle)
        };

    /// <summary>
    /// 转成基类迁移表的注册键（动作名 = 枚举 ToString），供 LoadPort 模块实例注册自己的表；
    /// 机型可在返回值基础上增删后经 RegisterTransitions/AddTransition 定制。
    /// </summary>
    public static IReadOnlyDictionary<(int? State, string Action), (int ExecutingState, int SuccessState)> ToModuleTable()
    {
        return Transitions.ToDictionary(kv => (kv.Key.State, kv.Key.Action.ToString()),kv => kv.Value);
    }
}
