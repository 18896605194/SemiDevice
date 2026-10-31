using xyz.Elements.Enums;
using xyz.Modules.Enums;

namespace xyz.Modules.StateMachines;

/// <summary>
/// 对准器动作与状态迁移规则表
/// </summary>
public static class AlignerStateTable
{
    public static IReadOnlyDictionary<(int? State, string Action), (int ExecutingState, int SuccessState)> Transitions { get; } =
        new Dictionary<(int? State, string Action), (int ExecutingState, int SuccessState)>
        {
            // (当前状态, 动作) = (执行状态, 成功状态)
            [(ModuleState.NotInit, AlignerAction.Home)] = (AlignerState.Homing, ModuleState.Idle),
            [(ModuleState.Idle, AlignerAction.Home)] = (AlignerState.Homing, ModuleState.Idle),
            [(ModuleState.Error, AlignerAction.Home)] = (AlignerState.Homing, ModuleState.Idle),

            // 片放上来、机械手缩回以后（这一轮交互收尾时）接着对准，对完才回待命，调度这时候才来取
            [(TransferModuleState.TransferComplete, AlignerAction.Align)] = (AlignerState.Aligning, ModuleState.Idle),

            // 复位只清设备报错。出错时台上的片方向不知道，落未初始化，要回零（台上有片会再对一遍）
            [(ModuleState.NotInit, AlignerAction.Reset)] = (ModuleState.NotInit, ModuleState.NotInit),
            [(ModuleState.Idle, AlignerAction.Reset)] = (ModuleState.Idle, ModuleState.Idle),
            [(ModuleState.Error, AlignerAction.Reset)] = (ModuleState.Error, ModuleState.NotInit),

            // 卡在交互环里（取放片失败停在半路）也能复位：片到底在手上还是在台上要人工确认，所以落未初始化、回零时按账再对
            [(TransferModuleState.PreTransfer, AlignerAction.Reset)] = (TransferModuleState.PreTransfer, ModuleState.NotInit),
            [(TransferModuleState.TransferReady, AlignerAction.Reset)] = (TransferModuleState.TransferReady, ModuleState.NotInit),
            [(TransferModuleState.Transferring, AlignerAction.Reset)] = (TransferModuleState.Transferring, ModuleState.NotInit),
            [(TransferModuleState.TransferComplete, AlignerAction.Reset)] = (TransferModuleState.TransferComplete, ModuleState.NotInit),

            [(ModuleState.Idle, AlignerAction.Abort)] = (ModuleState.Aborting, ModuleState.Idle),
            [(ModuleState.Error, AlignerAction.Abort)] = (ModuleState.Aborting, ModuleState.Error),

            // null 表示任意当前状态：回零、对准半路停下，片的方向不知道
            [(null, AlignerAction.Abort)] = (ModuleState.Aborting, ModuleState.NotInit),
        };
}
