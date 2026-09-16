namespace xyz.Modules;

/// <summary>
/// E87 的端口搬运状态（Host 看到的那一套）：由 EAP 侧状态机维护，设备侧只在 E84 握手时查询。
/// </summary>
public enum LoadPortTransferState
{
    /// <summary>不可用：停用、报错或未回原点。</summary>
    OutOfService,

    /// <summary>可用，但这会儿不接受交接。</summary>
    InService,

    /// <summary>可交接，方向未定。</summary>
    TransferReady,

    /// <summary>等着送盒进来。</summary>
    ReadyToLoad,

    /// <summary>等着把盒取走。</summary>
    ReadyToUnload,

    /// <summary>交接被挡住：正在取放片、门开着、端口被占等。</summary>
    TransferBlocked,
}
