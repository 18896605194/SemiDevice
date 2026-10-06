namespace xyz.Components.Enums;

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
