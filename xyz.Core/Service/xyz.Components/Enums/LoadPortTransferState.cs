namespace xyz.Components.Enums;

/// <summary>
/// E87 的端口搬运状态
/// </summary>
public enum LoadPortTransferState
{
    /// <summary>
    /// 不可用
    /// </summary>
    OutOfService,

    /// <summary>
    /// 可用，但这会儿不接受交接
    /// </summary>
    InService,

    /// <summary>
    /// 可交接
    /// </summary>
    TransferReady,

    /// <summary>
    /// 等着送盒进来
    /// </summary>
    ReadyToLoad,

    /// <summary>
    /// 等着把盒取走
    /// </summary>
    ReadyToUnload,

    /// <summary>
    /// 交接被挡住
    /// </summary>
    TransferBlocked,
}
