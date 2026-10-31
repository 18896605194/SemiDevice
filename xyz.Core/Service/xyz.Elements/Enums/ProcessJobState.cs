namespace xyz.Elements.Enums;

/// <summary>
/// PJ（Process Job）状态，照 SEMI E40。数值就是 E40-1101 起的 PRJobState，上报 Host 用这个数（5 是标准里保留的）。
/// SETTING UP、WAITING FOR START、PROCESSING 合称"执行中"（EXECUTING）；PAUSING、PAUSED 合称"暂停"（PAUSE）
/// </summary>
public class ProcessJobState
{
    /// <summary>
    /// 内部初始状态：对象已创建，尚未入队；不向 Host 上报
    /// </summary>
    public const int Created = -1;

    /// <summary>
    /// QUEUED/POOLED：建好了，等 CJ 启动它
    /// </summary>
    public const int QueuedPooled = 0;

    /// <summary>
    /// SETTING UP：CJ 启动了它，在核对片、载具、配方快照
    /// </summary>
    public const int SettingUp = 1;

    /// <summary>
    /// WAITING FOR START：准备好了，等 Start 命令（手动启动的 PJ）
    /// </summary>
    public const int WaitingForStart = 2;

    /// <summary>
    /// PROCESSING：在投片、在做
    /// </summary>
    public const int Processing = 3;

    /// <summary>
    /// PROCESS COMPLETE：片都做完了，还在回片
    /// </summary>
    public const int ProcessComplete = 4;

    /// <summary>
    /// PAUSING：不再投新片，机内这个 PJ 的片照常走完
    /// </summary>
    public const int Pausing = 6;

    /// <summary>
    /// PAUSED：机内没有这个 PJ 的片了，等恢复
    /// </summary>
    public const int Paused = 7;

    /// <summary>
    /// STOPPING：不再投新片，机内的走完就结束，没投的记未执行
    /// </summary>
    public const int Stopping = 8;

    /// <summary>
    /// ABORTING：撤单、中止在途动作，等设备确认、片位核对
    /// </summary>
    public const int Aborting = 9;

    /// <summary>
    /// STOPPED：停止做完（结束前的状态值）
    /// </summary>
    public const int Stopped = 10;

    /// <summary>
    /// ABORTED：中止做完（结束前的状态值）
    /// </summary>
    public const int Aborted = 11;
}
