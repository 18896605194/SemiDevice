namespace xyz.Modules;

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
