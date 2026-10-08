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

            // Reset 只清错、不动机构，不占用独立执行状态。没初始化、出过错的复位完还是没初始化，要再 Home 一次（照老 CTC）。
            // Loaded 复位完表里写最保守的 NotInit，模块再按状态查询的门位改：门开着 → Loaded，门关着 → Idle，查不到 → 保持 NotInit
            // （BaseLoadPortModule.SetStateByDoor）。Idle 一律当"门关好、没 Load"用（E87、E84 据此判能不能取走），门不确定就不能落 Idle。
            [(ModuleState.NotInit, LoadPortAction.Reset)] = (ModuleState.NotInit, ModuleState.NotInit),
            [(ModuleState.Idle, LoadPortAction.Reset)] = (ModuleState.Idle, ModuleState.Idle),
            [(ModuleState.Error, LoadPortAction.Reset)] = (ModuleState.Error, ModuleState.NotInit),
            [(LoadPortState.Loaded, LoadPortAction.Reset)] = (LoadPortState.Loaded, ModuleState.NotInit),

            // Clamp/Unclamp 只在空闲（门关）时允许；Loaded（门开）不允许松开。
            [(ModuleState.Idle, LoadPortAction.Clamp)] = (LoadPortState.Clamping, ModuleState.Idle),
            [(ModuleState.Idle, LoadPortAction.Unclamp)] = (LoadPortState.Unclamping, ModuleState.Idle),

            // Abort 只停、能顶替任何在途动作。空闲的中止完还是空闲；出错的中止完还是出错（不然点一下中止就绕过了复位和 Home）。
            [(ModuleState.Idle, LoadPortAction.Abort)] = (ModuleState.Aborting, ModuleState.Idle),
            [(ModuleState.Error, LoadPortAction.Abort)] = (ModuleState.Aborting, ModuleState.Error),

            // 其余（null 表示任意当前状态）一律 NotInit，要人 Home：没初始化的、打断了 Load / Unload / Home / 夹紧松开的
            // （门、夹爪可能停在半路）。Loaded、正被机械手取放的（门开着没在动）由模块按门位改成 Loaded / Idle，查不到保持 NotInit。
            [(null, LoadPortAction.Abort)] = (ModuleState.Aborting, ModuleState.NotInit)
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
