namespace xyz.Modules;

/// <summary>
/// LoadPort 每一拍给 E84 的结论：由端口综合 Auto/Manual、上线、搬运状态算好（接了 EAP 以 EAP 为准），E84 只照着做。
/// </summary>
public enum E84Permit
{
    /// <summary>
    /// 不可交接（Manual、下线、Out Of Service 等）：HO_AVBL 灭，进行中的交接按中止处理。
    /// </summary>
    NotAvailable,

    /// <summary>
    /// 可交接，但这会儿不送也不取（端口在忙、这一盒还没干完）：HO_AVBL 亮，不亮 L_REQ/U_REQ。
    /// </summary>
    Blocked,

    /// <summary>
    /// 等送盒进来。
    /// </summary>
    ReadyToLoad,

    /// <summary>
    /// 等把盒取走。
    /// </summary>
    ReadyToUnload,
}
