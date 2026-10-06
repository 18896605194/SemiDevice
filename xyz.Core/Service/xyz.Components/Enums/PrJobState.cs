namespace xyz.Components.Enums;

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
