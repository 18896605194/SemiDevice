namespace xyz.Components.Enums;

/// <summary>
/// PJ 命令（E40 PRJobCommand，Host 的 S16F5 PRCMDNAME；本地界面一样用）。
/// </summary>
public enum ProcessJobCommand
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
