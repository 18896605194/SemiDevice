namespace xyz.Modules;

/// <summary>
/// E84 状态
/// </summary>
public enum E84State
{
    /// <summary>
    /// 不可交接：端口是 Manual 或 Out Of Service，以后会有光栅尺
    /// </summary>
    NotAvailable,

    /// <summary>
    /// 可交接：HO_AVBL 亮着，等天车选中
    /// </summary>
    Available,

    /// <summary>
    /// 已亮 L_REQ/U_REQ，等天车 TR_REQ（TP1）。
    /// </summary>
    Requesting,

    /// <summary>
    /// 已给 READY、交接开始，等天车车 BUSY（TP2）。
    /// </summary>
    WaitBusy,

    /// <summary>
    /// 搬运中，等载具放上/取走（TP3）。
    /// </summary>
    Transferring,

    /// <summary>
    /// 已撤 L_REQ/U_REQ，等天车撤 BUSY、给 COMPT（TP4）。
    /// </summary>
    WaitComplete,

    /// <summary>
    /// 已撤 READY，等天车撤 VALID 等信号（TP5），撤完算交接完成。
    /// </summary>
    Releasing,

    /// <summary>
    /// 某一段超时
    /// </summary>
    TimedOut,
}
