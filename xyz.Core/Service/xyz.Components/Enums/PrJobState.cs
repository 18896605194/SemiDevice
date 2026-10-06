namespace xyz.Modules;

/// <summary>
/// PJ（Process Job）状态，照 SEMI E40。数值就是 E40-1101 起的 PRJobState，上报 Host 用这个数（5 是标准里保留的）。
/// SETTING UP、WAITING FOR START、PROCESSING 合称"执行中"（EXECUTING）；PAUSING、PAUSED 合称"暂停"（PAUSE）。
/// </summary>
public enum PrJobState
{
    /// <summary>QUEUED/POOLED：建好了，等 CJ 启动它。</summary>
    QueuedPooled = 0,

    /// <summary>SETTING UP：CJ 启动了它，在核对片、载具、配方快照。</summary>
    SettingUp = 1,

    /// <summary>WAITING FOR START：准备好了，等 Start 命令（手动启动的 PJ）。</summary>
    WaitingForStart = 2,

    /// <summary>PROCESSING：在投片、在做。</summary>
    Processing = 3,

    /// <summary>PROCESS COMPLETE：片都做完了，还在回片。</summary>
    ProcessComplete = 4,

    /// <summary>PAUSING：不再投新片，机内这个 PJ 的片照常走完。</summary>
    Pausing = 6,

    /// <summary>PAUSED：机内没有这个 PJ 的片了，等恢复。</summary>
    Paused = 7,

    /// <summary>STOPPING：不再投新片，机内的走完就结束，没投的记未执行。</summary>
    Stopping = 8,

    /// <summary>ABORTING：撤单、中止在途动作，等设备确认、片位核对。</summary>
    Aborting = 9,

    /// <summary>STOPPED：停止做完（结束前的状态值）。</summary>
    Stopped = 10,

    /// <summary>ABORTED：中止做完（结束前的状态值）。</summary>
    Aborted = 11,
}

/// <summary>
/// PJ 命令（E40 PRJobCommand，Host 的 S16F5 PRCMDNAME；本地界面一样用）。
/// </summary>
public enum PrJobCommand
{
    /// <summary>START：WAITING FOR START → PROCESSING（#5）。</summary>
    Start,

    /// <summary>PAUSE：执行中 → PAUSING（#8）。</summary>
    Pause,

    /// <summary>RESUME：暂停 → 暂停前的执行状态（#10）。</summary>
    Resume,

    /// <summary>STOP：执行中 / 暂停 → STOPPING（#11 / #12）；排队的直接删（#18）。</summary>
    Stop,

    /// <summary>ABORT：执行中 / STOPPING / 暂停 → ABORTING（#13 / #14 / #15）；排队的直接删（#18）。</summary>
    Abort,

    /// <summary>CANCEL：排队的删掉（#18）。</summary>
    Cancel,
}

/// <summary>
/// 推动 PJ 状态转换的事：命令和设备侧的进展都在这儿，每个对应 E40 的一条（或几条）转换。
/// </summary>
public enum PrJobTrigger
{
    /// <summary>#2：CJ 启动这个 PJ。</summary>
    Setup,

    /// <summary>#3：准备好了，要等 Start（手动启动）。</summary>
    SetupDoneWait,

    /// <summary>#4：准备好了，直接开始（自动启动）。</summary>
    SetupDoneStart,

    /// <summary>#5：Start 命令。</summary>
    Start,

    /// <summary>#6：片都加工完了。</summary>
    ProcessDone,

    /// <summary>#7：片都回去了，结束。</summary>
    MaterialOut,

    /// <summary>#8：Pause 命令。</summary>
    Pause,

    /// <summary>#9：机内没有这个 PJ 的片了，暂停到位。</summary>
    PauseDone,

    /// <summary>#10：Resume 命令。</summary>
    Resume,

    /// <summary>#11 / #12：Stop 命令。</summary>
    Stop,

    /// <summary>#13 / #14 / #15：Abort 命令。</summary>
    Abort,

    /// <summary>#16：中止做完（在途动作都结束、片位确定）。</summary>
    AbortDone,

    /// <summary>#17：停止做完（机内的片都回去了）。</summary>
    StopDone,

    /// <summary>#18：排队时被 Cancel / Stop / Abort。</summary>
    Dequeue,
}
