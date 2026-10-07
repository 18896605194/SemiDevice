using xyz.Components.Enums;

namespace xyz.Modules.StateMachines;

/// <summary>CJ 状态表；key 为当前状态和 action，value 为对应的状态转换。</summary>
public static class ControlJobStateTable
{
    public static IReadOnlyDictionary<
        (ControlJobState State, ControlStateAction Action),
        StateTransition<ControlJobState>> Transitions { get; } =
        new Dictionary<(ControlJobState State, ControlStateAction Action), StateTransition<ControlJobState>>
        {
            [(ControlJobState.Create, ControlStateAction.Create)] = new(ControlJobState.Queued, 1),
            [(ControlJobState.Queued, ControlStateAction.Dequeue)] = new(ControlJobState.Queued, 2, true),
            [(ControlJobState.Queued, ControlStateAction.Select)] = new(ControlJobState.Selected, 3),
            [(ControlJobState.Selected, ControlStateAction.Deselect)] = new(ControlJobState.Queued, 4),
            [(ControlJobState.Selected, ControlStateAction.MaterialReadyStart)] = new(ControlJobState.Executing, 5),
            [(ControlJobState.Selected, ControlStateAction.MaterialReadyWait)] = new(ControlJobState.WaitingForStart, 6),
            [(ControlJobState.WaitingForStart, ControlStateAction.Start)] = new(ControlJobState.Executing, 7),
            [(ControlJobState.Executing, ControlStateAction.Pause)] = new(ControlJobState.Paused, 8),
            [(ControlJobState.Paused, ControlStateAction.Resume)] = new(ControlJobState.Executing, 9),
            [(ControlJobState.Executing, ControlStateAction.AllDone)] = new(ControlJobState.Completed, 10),
            [(ControlJobState.Selected, ControlStateAction.Stopped)] = new(ControlJobState.Completed, 11),
            [(ControlJobState.WaitingForStart, ControlStateAction.Stopped)] = new(ControlJobState.Completed, 11),
            [(ControlJobState.Executing, ControlStateAction.Stopped)] = new(ControlJobState.Completed, 11),
            [(ControlJobState.Paused, ControlStateAction.Stopped)] = new(ControlJobState.Completed, 11),
            [(ControlJobState.Selected, ControlStateAction.Aborted)] = new(ControlJobState.Completed, 12),
            [(ControlJobState.WaitingForStart, ControlStateAction.Aborted)] = new(ControlJobState.Completed, 12),
            [(ControlJobState.Executing, ControlStateAction.Aborted)] = new(ControlJobState.Completed, 12),
            [(ControlJobState.Paused, ControlStateAction.Aborted)] = new(ControlJobState.Completed, 12),
            [(ControlJobState.Completed, ControlStateAction.Delete)] = new(ControlJobState.Completed, 13, true),
        };
}
