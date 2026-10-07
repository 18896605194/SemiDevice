using xyz.Components.Enums;
using xyz.Modules.StateMachines;

namespace xyz.Modules;

/// <summary>PJ 状态机；保留 E40 子状态，每个 PJ 使用自己的转换表。</summary>
public sealed class PjStateMachine : BaseStateMachine<ProcessJobState, ProcessStateAction>
{
    public PjStateMachine()
    {
        CurrentState = ProcessJobState.Created;
        Transitions = BuildTransitions();
        foreach (var state in new[] { ProcessJobState.SettingUp, ProcessJobState.WaitingForStart, ProcessJobState.Processing })
        {
            // Pause 入口在基类的转换锁内保存恢复目标，每个 PJ 的表互相独立。
            Transitions[(state, ProcessStateAction.Pause)].OnEntry = _ =>
            {
                Transitions[(ProcessJobState.Pausing, ProcessStateAction.Resume)].TargetState = state;
                Transitions[(ProcessJobState.Paused, ProcessStateAction.Resume)].TargetState = state;
            };
        }
    }

    #region PJ 状态表

    private static Dictionary<(ProcessJobState, ProcessStateAction), StateTransition<ProcessJobState>> BuildTransitions()
    {
        return new Dictionary<(ProcessJobState, ProcessStateAction), StateTransition<ProcessJobState>>
        {
            [(ProcessJobState.Created, ProcessStateAction.Queue)] =
                new() { TargetState = ProcessJobState.QueuedPooled },
            [(ProcessJobState.QueuedPooled, ProcessStateAction.Setup)] =
                new() { TargetState = ProcessJobState.SettingUp },
            [(ProcessJobState.SettingUp, ProcessStateAction.WaitForStart)] =
                new() { TargetState = ProcessJobState.WaitingForStart },
            [(ProcessJobState.SettingUp, ProcessStateAction.Activate)] =
                new() { TargetState = ProcessJobState.Processing },
            [(ProcessJobState.WaitingForStart, ProcessStateAction.Start)] =
                new() { TargetState = ProcessJobState.Processing },
            [(ProcessJobState.Processing, ProcessStateAction.Complete)] =
                new() { TargetState = ProcessJobState.ProcessComplete },
            [(ProcessJobState.ProcessComplete, ProcessStateAction.Finish)] =
                new() { TargetState = ProcessJobState.ProcessComplete },
            [(ProcessJobState.SettingUp, ProcessStateAction.Pause)] =
                new() { TargetState = ProcessJobState.Pausing },
            [(ProcessJobState.WaitingForStart, ProcessStateAction.Pause)] =
                new() { TargetState = ProcessJobState.Pausing },
            [(ProcessJobState.Processing, ProcessStateAction.Pause)] =
                new() { TargetState = ProcessJobState.Pausing },
            [(ProcessJobState.Pausing, ProcessStateAction.FinishPause)] =
                new() { TargetState = ProcessJobState.Paused },
            [(ProcessJobState.Pausing, ProcessStateAction.Resume)] =
                new() { TargetState = ProcessJobState.Processing },
            [(ProcessJobState.Paused, ProcessStateAction.Resume)] =
                new() { TargetState = ProcessJobState.Processing },
            [(ProcessJobState.SettingUp, ProcessStateAction.Stop)] =
                new() { TargetState = ProcessJobState.Stopping },
            [(ProcessJobState.WaitingForStart, ProcessStateAction.Stop)] =
                new() { TargetState = ProcessJobState.Stopping },
            [(ProcessJobState.Processing, ProcessStateAction.Stop)] =
                new() { TargetState = ProcessJobState.Stopping },
            [(ProcessJobState.Pausing, ProcessStateAction.Stop)] =
                new() { TargetState = ProcessJobState.Stopping },
            [(ProcessJobState.Paused, ProcessStateAction.Stop)] =
                new() { TargetState = ProcessJobState.Stopping },
            [(ProcessJobState.SettingUp, ProcessStateAction.Abort)] =
                new() { TargetState = ProcessJobState.Aborting },
            [(ProcessJobState.WaitingForStart, ProcessStateAction.Abort)] =
                new() { TargetState = ProcessJobState.Aborting },
            [(ProcessJobState.Processing, ProcessStateAction.Abort)] =
                new() { TargetState = ProcessJobState.Aborting },
            [(ProcessJobState.Pausing, ProcessStateAction.Abort)] =
                new() { TargetState = ProcessJobState.Aborting },
            [(ProcessJobState.Paused, ProcessStateAction.Abort)] =
                new() { TargetState = ProcessJobState.Aborting },
            [(ProcessJobState.Stopping, ProcessStateAction.Abort)] =
                new() { TargetState = ProcessJobState.Aborting },
            [(ProcessJobState.Aborting, ProcessStateAction.FinishAbort)] =
                new() { TargetState = ProcessJobState.Aborted },
            [(ProcessJobState.Stopping, ProcessStateAction.FinishStop)] =
                new() { TargetState = ProcessJobState.Stopped },
            [(ProcessJobState.QueuedPooled, ProcessStateAction.Dequeue)] =
                new() { TargetState = ProcessJobState.QueuedPooled },
        };
    }

    #endregion
}
