namespace xyz.Modules;

/// <summary>
/// E84 组件当前走到哪一步。
/// </summary>
public enum E84State
{
    /// <summary>
    /// 不可交接：E84 没开（EC）、端口不是 Auto、Out Of Service 或光幕被挡；输出全灭。
    /// </summary>
    NotAvailable,

    /// <summary>
    /// 可交接：HO_AVBL 亮着，等搬运车选中本端口。
    /// </summary>
    Available,

    /// <summary>
    /// 已亮 L_REQ/U_REQ，等搬运车 TR_REQ。
    /// </summary>
    Requesting,

    /// <summary>
    /// 送盒交接中（READY 已给出）。
    /// </summary>
    Loading,

    /// <summary>
    /// 取盒交接中（READY 已给出）。
    /// </summary>
    Unloading,

    /// <summary>
    /// 某段握手超时：输出全灭并锁住，等人工 Retry 或 Complete。
    /// </summary>
    TimedOut,
}
