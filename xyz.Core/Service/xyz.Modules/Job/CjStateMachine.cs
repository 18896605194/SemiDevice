using xyz.Components.Enums;
using xyz.Modules.StateMachines;

namespace xyz.Modules;

/// <summary>CJ 状态机；只维护状态和转换表，不持有 ControlJob 或 PJ。</summary>
public sealed class CjStateMachine : BaseStateMachine<ControlJobState, ControlStateAction>
{
    public CjStateMachine()
    {
        CurrentState = ControlJobState.Created;
        Transitions = BuildTransitions();
    }

    #region CJ 状态表

    private static Dictionary<(ControlJobState, ControlStateAction), StateTransition<ControlJobState>> BuildTransitions()
    {
        return new Dictionary<(ControlJobState, ControlStateAction), StateTransition<ControlJobState>>
        {
            [(ControlJobState.Created, ControlStateAction.Queue)] =
                new() { TargetState = ControlJobState.Queued },
            [(ControlJobState.Queued, ControlStateAction.Select)] =
                new() { TargetState = ControlJobState.Selected },
            [(ControlJobState.Selected, ControlStateAction.Activate)] =
                new() { TargetState = ControlJobState.Executing },
            [(ControlJobState.Selected, ControlStateAction.Deselect)] =
                new() { TargetState = ControlJobState.Queued },
            [(ControlJobState.Executing, ControlStateAction.Rollback)] =
                new() { TargetState = ControlJobState.Selected },
            [(ControlJobState.Executing, ControlStateAction.Pause)] =
                new() { TargetState = ControlJobState.Paused },
            [(ControlJobState.Paused, ControlStateAction.Resume)] =
                new() { TargetState = ControlJobState.Executing },
            [(ControlJobState.Executing, ControlStateAction.Complete)] =
                new() { TargetState = ControlJobState.Completed },
            [(ControlJobState.Paused, ControlStateAction.Complete)] =
                new() { TargetState = ControlJobState.Completed },

            // 中止：先停止推进，执行收尾后进入独立的 Aborted 终态。
            [(ControlJobState.Created, ControlStateAction.Abort)] =
                new() { TargetState = ControlJobState.Aborting },
            [(ControlJobState.Queued, ControlStateAction.Abort)] =
                new() { TargetState = ControlJobState.Aborting },
            [(ControlJobState.Selected, ControlStateAction.Abort)] =
                new() { TargetState = ControlJobState.Aborting },
            [(ControlJobState.WaitingForStart, ControlStateAction.Abort)] =
                new() { TargetState = ControlJobState.Aborting },
            [(ControlJobState.Executing, ControlStateAction.Abort)] =
                new() { TargetState = ControlJobState.Aborting },
            [(ControlJobState.Paused, ControlStateAction.Abort)] =
                new() { TargetState = ControlJobState.Aborting },
            [(ControlJobState.Aborting, ControlStateAction.FinishAbort)] =
                new() { TargetState = ControlJobState.Aborted },

            // 参考表未接入 WaitingForStart；保留本设备由 SC 决定的手动启动路径。
            [(ControlJobState.Selected, ControlStateAction.WaitForStart)] =
                new() { TargetState = ControlJobState.WaitingForStart },
            [(ControlJobState.WaitingForStart, ControlStateAction.Activate)] =
                new() { TargetState = ControlJobState.Executing },

            // Stop 收尾以及 E94 #2 / #13 删除。
            [(ControlJobState.Selected, ControlStateAction.FinishStop)] =
                new() { TargetState = ControlJobState.Completed },
            [(ControlJobState.WaitingForStart, ControlStateAction.FinishStop)] =
                new() { TargetState = ControlJobState.Completed },
            [(ControlJobState.Executing, ControlStateAction.FinishStop)] =
                new() { TargetState = ControlJobState.Completed },
            [(ControlJobState.Paused, ControlStateAction.FinishStop)] =
                new() { TargetState = ControlJobState.Completed },
            [(ControlJobState.Queued, ControlStateAction.Dequeue)] =
                new() { TargetState = ControlJobState.Queued },
            [(ControlJobState.Completed, ControlStateAction.Delete)] =
                new() { TargetState = ControlJobState.Completed },
            [(ControlJobState.Aborted, ControlStateAction.Delete)] =
                new() { TargetState = ControlJobState.Aborted },
        };
    }

    #endregion
}
