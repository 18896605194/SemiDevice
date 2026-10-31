using xyz.Elements.Enums;

namespace xyz.Elements.Elements;

/// <summary>
/// CJ 动作与状态迁移规则表。CJ 的迁移都是当场做完的，所以执行态和成功态写一样
/// </summary>
public static class CjStateTable
{
    public static IReadOnlyDictionary<(int? State, string Action), (int ExecutingState, int SuccessState)> Transitions { get; } =
        new Dictionary<(int? State, string Action), (int ExecutingState, int SuccessState)>
        {
            // (当前状态, 动作) = (执行状态, 成功状态)
            [(ControlJobState.Created, ControlStateAction.Queue)] = (ControlJobState.Queued, ControlJobState.Queued),
            [(ControlJobState.Queued, ControlStateAction.Select)] = (ControlJobState.Selected, ControlJobState.Selected),
            [(ControlJobState.Selected, ControlStateAction.Activate)] = (ControlJobState.Executing, ControlJobState.Executing),
            [(ControlJobState.Selected, ControlStateAction.Deselect)] = (ControlJobState.Queued, ControlJobState.Queued),
            [(ControlJobState.Executing, ControlStateAction.Rollback)] = (ControlJobState.Selected, ControlJobState.Selected),
            [(ControlJobState.Executing, ControlStateAction.Pause)] = (ControlJobState.Paused, ControlJobState.Paused),
            [(ControlJobState.Paused, ControlStateAction.Resume)] = (ControlJobState.Executing, ControlJobState.Executing),
            [(ControlJobState.Executing, ControlStateAction.Complete)] = (ControlJobState.Completed, ControlJobState.Completed),
            [(ControlJobState.Paused, ControlStateAction.Complete)] = (ControlJobState.Completed, ControlJobState.Completed),

            // 中止：先停止推进，执行收尾后进入独立的 Aborted 终态
            [(ControlJobState.Created, ControlStateAction.Abort)] = (ControlJobState.Aborting, ControlJobState.Aborting),
            [(ControlJobState.Queued, ControlStateAction.Abort)] = (ControlJobState.Aborting, ControlJobState.Aborting),
            [(ControlJobState.Selected, ControlStateAction.Abort)] = (ControlJobState.Aborting, ControlJobState.Aborting),
            [(ControlJobState.WaitingForStart, ControlStateAction.Abort)] = (ControlJobState.Aborting, ControlJobState.Aborting),
            [(ControlJobState.Executing, ControlStateAction.Abort)] = (ControlJobState.Aborting, ControlJobState.Aborting),
            [(ControlJobState.Paused, ControlStateAction.Abort)] = (ControlJobState.Aborting, ControlJobState.Aborting),
            [(ControlJobState.Aborting, ControlStateAction.FinishAbort)] = (ControlJobState.Aborted, ControlJobState.Aborted),

            // 参考表未接入 WaitingForStart；保留本设备由 machine.xml 决定的手动启动路径
            [(ControlJobState.Selected, ControlStateAction.WaitForStart)] = (ControlJobState.WaitingForStart, ControlJobState.WaitingForStart),
            [(ControlJobState.WaitingForStart, ControlStateAction.Activate)] = (ControlJobState.Executing, ControlJobState.Executing),

            // Stop 收尾以及 E94 #2 / #13 删除
            [(ControlJobState.Selected, ControlStateAction.FinishStop)] = (ControlJobState.Completed, ControlJobState.Completed),
            [(ControlJobState.WaitingForStart, ControlStateAction.FinishStop)] = (ControlJobState.Completed, ControlJobState.Completed),
            [(ControlJobState.Executing, ControlStateAction.FinishStop)] = (ControlJobState.Completed, ControlJobState.Completed),
            [(ControlJobState.Paused, ControlStateAction.FinishStop)] = (ControlJobState.Completed, ControlJobState.Completed),
            [(ControlJobState.Queued, ControlStateAction.Dequeue)] = (ControlJobState.Queued, ControlJobState.Queued),
            [(ControlJobState.Completed, ControlStateAction.Delete)] = (ControlJobState.Completed, ControlJobState.Completed),
            [(ControlJobState.Aborted, ControlStateAction.Delete)] = (ControlJobState.Aborted, ControlJobState.Aborted),
        };
}
