namespace xyz.Modules;

/// <summary>
/// LoadPort 单条驱动指令动作（LoadPortCommandOperation）的两步。
/// </summary>
public enum LoadPortCommandStep
{
    /// <summary>发指令（提交了就算这一步完成）。</summary>
    SendCommand,

    /// <summary>等指令完结（每拍看一眼，超时判失败）。</summary>
    WaitCommand,
}
