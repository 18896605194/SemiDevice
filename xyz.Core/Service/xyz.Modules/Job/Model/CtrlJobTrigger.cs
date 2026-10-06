namespace xyz.Modules;

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
