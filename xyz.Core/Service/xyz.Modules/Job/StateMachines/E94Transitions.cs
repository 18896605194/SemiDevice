using xyz.Components.Enums;

namespace xyz.Modules;

/// <summary>
/// SEMI E94 CJ 状态转换表（#1~#13），一行一条，号码照标准。
/// #1（建好进 QUEUED）没有出发状态，建 CJ 时直接报，不在表里。
/// </summary>
public static class E94Transitions
{
    /// <summary>#1：建好，进 QUEUED。</summary>
    public const int Created = 1;

    /// <summary>#12：中止做完，进 COMPLETED。</summary>
    public const int Aborted = 12;

    /// <summary>#13：完成后删掉。</summary>
    public const int Deleted = 13;

    /// <summary>ACTIVE 超状态：SELECTED、WAITING FOR START、EXECUTING、PAUSED。</summary>
    public static IReadOnlyList<CtrlJobState> Active { get; } =
        [CtrlJobState.Selected, CtrlJobState.WaitingForStart, CtrlJobState.Executing, CtrlJobState.Paused];

    public static TransitionTable<CtrlJobState, CtrlJobTrigger> Table { get; } = new TransitionTable<CtrlJobState, CtrlJobTrigger>()
        .AddEnd(CtrlJobState.Queued, CtrlJobTrigger.Dequeue, CtrlJobState.Queued, 2)
        .Add(CtrlJobState.Queued, CtrlJobTrigger.Select, CtrlJobState.Selected, 3)
        .Add(CtrlJobState.Selected, CtrlJobTrigger.Deselect, CtrlJobState.Queued, 4)
        .Add(CtrlJobState.Selected, CtrlJobTrigger.MaterialReadyStart, CtrlJobState.Executing, 5)
        .Add(CtrlJobState.Selected, CtrlJobTrigger.MaterialReadyWait, CtrlJobState.WaitingForStart, 6)
        .Add(CtrlJobState.WaitingForStart, CtrlJobTrigger.Start, CtrlJobState.Executing, 7)
        .Add(CtrlJobState.Executing, CtrlJobTrigger.Pause, CtrlJobState.Paused, 8)
        .Add(CtrlJobState.Paused, CtrlJobTrigger.Resume, CtrlJobState.Executing, 9)
        .Add(CtrlJobState.Executing, CtrlJobTrigger.AllDone, CtrlJobState.Completed, 10)
        .Add(Active, CtrlJobTrigger.Stopped, CtrlJobState.Completed, 11)
        .Add(Active, CtrlJobTrigger.Aborted, CtrlJobState.Completed, 12)
        .AddEnd(CtrlJobState.Completed, CtrlJobTrigger.Delete, CtrlJobState.Completed, 13);
}
