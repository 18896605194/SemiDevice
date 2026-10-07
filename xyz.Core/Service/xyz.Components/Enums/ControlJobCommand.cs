namespace xyz.Components.Enums;

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
