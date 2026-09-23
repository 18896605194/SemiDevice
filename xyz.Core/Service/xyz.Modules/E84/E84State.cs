namespace xyz.Modules;

/// <summary>
/// E84 组件当前走到哪一步（组件按它 switch 推进；带计时的步各对应一段 TP）。
/// </summary>
public enum E84State
{
    /// <summary>
    /// 不可交接：E84 没开（EC）、端口许可 NotAvailable（Manual、下线、Out Of Service）或光幕被挡；输出全灭。
    /// </summary>
    NotAvailable,

    /// <summary>
    /// 可交接：HO_AVBL 亮着，等搬运车选中本端口。
    /// </summary>
    Available,

    /// <summary>
    /// 已亮 L_REQ/U_REQ，等搬运车 TR_REQ（TP1）。
    /// </summary>
    Requesting,

    /// <summary>
    /// 已给 READY、交接开始，等搬运车 BUSY（TP2）。
    /// </summary>
    WaitBusy,

    /// <summary>
    /// 搬运中，等载具放上/取走（TP3）。
    /// </summary>
    Transferring,

    /// <summary>
    /// 已撤 L_REQ/U_REQ，等搬运车撤 BUSY、给 COMPT（TP4）。
    /// </summary>
    WaitComplete,

    /// <summary>
    /// 已撤 READY，等搬运车撤 VALID 等信号（TP5），撤完算交接完成。
    /// </summary>
    Releasing,

    /// <summary>
    /// 某段握手超时：输出全灭并锁住，等人工 Retry 或 Complete。
    /// </summary>
    TimedOut,
}
