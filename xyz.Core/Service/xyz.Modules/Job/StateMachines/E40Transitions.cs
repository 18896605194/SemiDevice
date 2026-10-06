using xyz.Components.Enums;

namespace xyz.Modules;

/// <summary>
/// SEMI E40 PJ 状态转换表（#1~#18），一行一条，号码照标准。
/// #1（建好进 QUEUED/POOLED）没有出发状态，建 PJ 时直接报，不在表里。
/// </summary>
public static class E40Transitions
{
    /// <summary>#1：建好，进 QUEUED/POOLED。</summary>
    public const int Created = 1;

    /// <summary>执行中（EXECUTING 超状态）：SETTING UP、WAITING FOR START、PROCESSING。</summary>
    public static IReadOnlyList<PrJobState> Executing { get; } =
        [PrJobState.SettingUp, PrJobState.WaitingForStart, PrJobState.Processing];

    /// <summary>暂停（PAUSE 超状态）：PAUSING、PAUSED。</summary>
    public static IReadOnlyList<PrJobState> Pause { get; } = [PrJobState.Pausing, PrJobState.Paused];

    public static TransitionTable<PrJobState, PrJobTrigger> Table { get; } = new TransitionTable<PrJobState, PrJobTrigger>()
        .Add(PrJobState.QueuedPooled, PrJobTrigger.Setup, PrJobState.SettingUp, 2)
        .Add(PrJobState.SettingUp, PrJobTrigger.SetupDoneWait, PrJobState.WaitingForStart, 3)
        .Add(PrJobState.SettingUp, PrJobTrigger.SetupDoneStart, PrJobState.Processing, 4)
        .Add(PrJobState.WaitingForStart, PrJobTrigger.Start, PrJobState.Processing, 5)
        .Add(PrJobState.Processing, PrJobTrigger.ProcessDone, PrJobState.ProcessComplete, 6)
        .AddEnd(PrJobState.ProcessComplete, PrJobTrigger.MaterialOut, PrJobState.ProcessComplete, 7)
        .Add(Executing, PrJobTrigger.Pause, PrJobState.Pausing, 8)
        .Add(PrJobState.Pausing, PrJobTrigger.PauseDone, PrJobState.Paused, 9)
        .AddResume(Pause, PrJobTrigger.Resume, 10)
        .Add(Executing, PrJobTrigger.Stop, PrJobState.Stopping, 11)
        .Add(Pause, PrJobTrigger.Stop, PrJobState.Stopping, 12)
        .Add(Executing, PrJobTrigger.Abort, PrJobState.Aborting, 13)
        .Add(PrJobState.Stopping, PrJobTrigger.Abort, PrJobState.Aborting, 14)
        .Add(Pause, PrJobTrigger.Abort, PrJobState.Aborting, 15)
        .AddEnd(PrJobState.Aborting, PrJobTrigger.AbortDone, PrJobState.Aborted, 16)
        .AddEnd(PrJobState.Stopping, PrJobTrigger.StopDone, PrJobState.Stopped, 17)
        .AddEnd(PrJobState.QueuedPooled, PrJobTrigger.Dequeue, PrJobState.QueuedPooled, 18);
}
