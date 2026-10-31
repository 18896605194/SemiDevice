using xyz.Elements.Enums;

namespace xyz.Elements.Elements;

/// <summary>
/// PJ 动作与状态迁移规则表，照 SEMI E40。PJ 的迁移都是当场做完的，所以执行态和成功态写一样。
/// Resume 这两条表里写的是 Processing，真正回哪个状态由 PjManager 按暂停前记下的那个来
/// </summary>
public static class PjStateTable
{
    public static IReadOnlyDictionary<(int? State, string Action), (int ExecutingState, int SuccessState)> Transitions { get; } =
        new Dictionary<(int? State, string Action), (int ExecutingState, int SuccessState)>
        {
            // (当前状态, 动作) = (执行状态, 成功状态)
            [(ProcessJobState.Created, ProcessStateAction.Queue)] = (ProcessJobState.QueuedPooled, ProcessJobState.QueuedPooled),
            [(ProcessJobState.QueuedPooled, ProcessStateAction.Setup)] = (ProcessJobState.SettingUp, ProcessJobState.SettingUp),
            [(ProcessJobState.SettingUp, ProcessStateAction.WaitForStart)] = (ProcessJobState.WaitingForStart, ProcessJobState.WaitingForStart),
            [(ProcessJobState.SettingUp, ProcessStateAction.Activate)] = (ProcessJobState.Processing, ProcessJobState.Processing),
            [(ProcessJobState.WaitingForStart, ProcessStateAction.Start)] = (ProcessJobState.Processing, ProcessJobState.Processing),
            [(ProcessJobState.Processing, ProcessStateAction.Complete)] = (ProcessJobState.ProcessComplete, ProcessJobState.ProcessComplete),
            [(ProcessJobState.ProcessComplete, ProcessStateAction.Finish)] = (ProcessJobState.ProcessComplete, ProcessJobState.ProcessComplete),

            // 暂停：从这三个状态进 Pausing，恢复时回到进来时的那个（PjManager 记着）
            [(ProcessJobState.SettingUp, ProcessStateAction.Pause)] = (ProcessJobState.Pausing, ProcessJobState.Pausing),
            [(ProcessJobState.WaitingForStart, ProcessStateAction.Pause)] = (ProcessJobState.Pausing, ProcessJobState.Pausing),
            [(ProcessJobState.Processing, ProcessStateAction.Pause)] = (ProcessJobState.Pausing, ProcessJobState.Pausing),
            [(ProcessJobState.Pausing, ProcessStateAction.FinishPause)] = (ProcessJobState.Paused, ProcessJobState.Paused),
            [(ProcessJobState.Pausing, ProcessStateAction.Resume)] = (ProcessJobState.Processing, ProcessJobState.Processing),
            [(ProcessJobState.Paused, ProcessStateAction.Resume)] = (ProcessJobState.Processing, ProcessJobState.Processing),

            [(ProcessJobState.SettingUp, ProcessStateAction.Stop)] = (ProcessJobState.Stopping, ProcessJobState.Stopping),
            [(ProcessJobState.WaitingForStart, ProcessStateAction.Stop)] = (ProcessJobState.Stopping, ProcessJobState.Stopping),
            [(ProcessJobState.Processing, ProcessStateAction.Stop)] = (ProcessJobState.Stopping, ProcessJobState.Stopping),
            [(ProcessJobState.Pausing, ProcessStateAction.Stop)] = (ProcessJobState.Stopping, ProcessJobState.Stopping),
            [(ProcessJobState.Paused, ProcessStateAction.Stop)] = (ProcessJobState.Stopping, ProcessJobState.Stopping),

            [(ProcessJobState.SettingUp, ProcessStateAction.Abort)] = (ProcessJobState.Aborting, ProcessJobState.Aborting),
            [(ProcessJobState.WaitingForStart, ProcessStateAction.Abort)] = (ProcessJobState.Aborting, ProcessJobState.Aborting),
            [(ProcessJobState.Processing, ProcessStateAction.Abort)] = (ProcessJobState.Aborting, ProcessJobState.Aborting),
            [(ProcessJobState.Pausing, ProcessStateAction.Abort)] = (ProcessJobState.Aborting, ProcessJobState.Aborting),
            [(ProcessJobState.Paused, ProcessStateAction.Abort)] = (ProcessJobState.Aborting, ProcessJobState.Aborting),
            [(ProcessJobState.Stopping, ProcessStateAction.Abort)] = (ProcessJobState.Aborting, ProcessJobState.Aborting),

            [(ProcessJobState.Aborting, ProcessStateAction.FinishAbort)] = (ProcessJobState.Aborted, ProcessJobState.Aborted),
            [(ProcessJobState.Stopping, ProcessStateAction.FinishStop)] = (ProcessJobState.Stopped, ProcessJobState.Stopped),
            [(ProcessJobState.QueuedPooled, ProcessStateAction.Dequeue)] = (ProcessJobState.QueuedPooled, ProcessJobState.QueuedPooled),
        };
}
