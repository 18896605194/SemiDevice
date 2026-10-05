namespace xyz.Modules;

/// <summary>
/// CJ（Control Job）状态，照 SEMI E94。数值就是 E94.1 的 State（0~5），上报 Host 用这个数。
/// SELECTED、WAITING FOR START、EXECUTING、PAUSED 合称 ACTIVE。CJ 没有"停止中""中止中"：
/// Stop / Abort 之后等下面的 PJ 都结束，再从 ACTIVE 进 COMPLETED（#11 / #12）。
/// </summary>
public enum CtrlJobState
{
    /// <summary>QUEUED：建好了，排队。</summary>
    Queued = 0,

    /// <summary>SELECTED：排到队首，资源留给它，等料。</summary>
    Selected = 1,

    /// <summary>WAITINGFORSTART：料到了，等 Start（手动启动的 CJ）。</summary>
    WaitingForStart = 2,

    /// <summary>EXECUTING：在跑，按顺序启动下面的 PJ。</summary>
    Executing = 3,

    /// <summary>PAUSED：不再启动新的 PJ（在跑的 PJ 不受影响）。</summary>
    Paused = 4,

    /// <summary>COMPLETED：结束了（正常 #10、停止 #11、中止 #12 看转换号）。</summary>
    Completed = 5,
}

/// <summary>
/// CJ 命令（E94 的 S16F27 CTLJOBCMD，数值照 E94.1；本地界面一样用）。
/// </summary>
public enum CtrlJobCommand
{
    /// <summary>CJStart：WAITINGFORSTART → EXECUTING（#7）。</summary>
    Start = 1,

    /// <summary>CJPause：EXECUTING → PAUSED（#8），只是不再启动新的 PJ。</summary>
    Pause = 2,

    /// <summary>CJResume：PAUSED → EXECUTING（#9）。</summary>
    Resume = 3,

    /// <summary>CJCancel：QUEUED → 删掉（#2），带 Action。</summary>
    Cancel = 4,

    /// <summary>CJDeselect：SELECTED → QUEUED（#4）。</summary>
    Deselect = 5,

    /// <summary>CJStop：QUEUED → 删掉（#2）；ACTIVE → 停下面的 PJ，都停完 → COMPLETED（#11）。带 Action。</summary>
    Stop = 6,

    /// <summary>CJAbort：QUEUED → 删掉（#2）；ACTIVE → 中止下面的 PJ，都中止完 → COMPLETED（#12）。带 Action。</summary>
    Abort = 7,

    /// <summary>CJHOQ：排队的 CJ 插到队首。</summary>
    HeadOfQueue = 8,
}

/// <summary>
/// CJ 的 Cancel / Stop / Abort 带的 Action（E94.1 CPVAL）：它名下还在排队的 PJ 留着还是删掉。
/// </summary>
public enum CtrlJobAction
{
    /// <summary>SAVEJOBS：排队的 PJ 留着（不再归这个 CJ）。</summary>
    SaveJobs = 0,

    /// <summary>REMOVEJOBS：排队的 PJ 一起删掉。</summary>
    RemoveJobs = 1,
}

/// <summary>
/// 推动 CJ 状态转换的事，每个对应 E94 的一条（或几条）转换。
/// </summary>
public enum CtrlJobTrigger
{
    /// <summary>#2：排队时被 Cancel / Stop / Abort。</summary>
    Dequeue,

    /// <summary>#3：排到队首、资源留给它。</summary>
    Select,

    /// <summary>#4：CJDeselect。</summary>
    Deselect,

    /// <summary>#5：料到了，自动启动。</summary>
    MaterialReadyStart,

    /// <summary>#6：料到了，等 Start。</summary>
    MaterialReadyWait,

    /// <summary>#7：CJStart。</summary>
    Start,

    /// <summary>#8：CJPause。</summary>
    Pause,

    /// <summary>#9：CJResume。</summary>
    Resume,

    /// <summary>#10：下面的 PJ 都正常结束。</summary>
    AllDone,

    /// <summary>#11：Stop 之后下面的 PJ 都结束了。</summary>
    Stopped,

    /// <summary>#12：Abort 之后下面的 PJ 都结束了。</summary>
    Aborted,

    /// <summary>#13：删掉（载具走了）。</summary>
    Delete,
}

/// <summary>
/// 命令是谁下的：本地界面、Host（EAP）、恢复。决定能不能下（Host 要在 Online Remote 下才收，以后接 E30 控制状态时用）。
/// </summary>
public enum JobCommandSource
{
    Local,
    Host,
    Recovery,
}
