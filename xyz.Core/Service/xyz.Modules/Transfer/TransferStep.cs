namespace xyz.Modules;

/// <summary>
/// 一趟搬运的步骤：源站点准备 → 取片 → 目标站点准备 → 放片。
/// 源和目标各自完整走一遍站点交互环（准备一 → 准备二 → 交互中 → 完成 → 回锚点态）。
/// </summary>
public enum TransferStep
{
    /// <summary>抢源站点（发准备一）。抢不到不算失败，下一拍接着抢，超时才判负。</summary>
    PrepareSource,

    WaitPrepareSource,

    /// <summary>源站点准备二：开门/放行，成功后站点进 TransferReady。</summary>
    PrepareSource2,

    WaitPrepareSource2,

    /// <summary>落"交互中"标记并发起取片。</summary>
    Pick,

    WaitPick,

    /// <summary>抢目标站点。</summary>
    PrepareTarget,

    WaitPrepareTarget,

    PrepareTarget2,

    WaitPrepareTarget2,

    /// <summary>落"交互中"标记并发起放片。</summary>
    Place,

    WaitPlace,
}
