namespace xyz.Components.Enums;

/// <summary>
/// CJ（Control Job）状态。Create 是内部初始状态；Queued 到 Completed 的数值（0~5）用于 E94 上报。
/// SELECTED、WAITING FOR START、EXECUTING、PAUSED 合称 ACTIVE。CJ 没有"停止中""中止中"：
/// Stop / Abort 之后等下面的 PJ 都结束，再从 ACTIVE 进 COMPLETED（#11 / #12）。
/// </summary>
public enum ControlJobState
{
    /// <summary>对象已创建，尚未加入 CJ 管理。</summary>
    Create = -1,

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
