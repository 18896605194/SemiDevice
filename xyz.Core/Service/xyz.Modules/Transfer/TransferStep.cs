namespace xyz.Modules;

/// <summary>
/// 一趟搬运的步骤：先抢目标站点（准备一）→ 源站点准备 → 取片 → 源站点收尾 → 目标站点准备二 → 放片 → 目标站点收尾。
/// 先抢目标是为了"放不下就不取"：目标没准备好时片还在源槽里，不会拿在手上干等、等超时。
/// 源和目标是同一个站点（同一个花篮里换槽）时只抢一次环，从源站点准备开始，取完不收尾接着放。
/// </summary>
public enum TransferStep
{
    /// <summary>抢目标站点（发准备一）。抢不到不算失败，下一拍接着抢，超时才判负。</summary>
    PrepareTarget,

    WaitPrepareTarget,

    /// <summary>抢源站点（发准备一）。</summary>
    PrepareSource,

    WaitPrepareSource,

    /// <summary>源站点准备二：开门/放行，成功后站点进 TransferReady。</summary>
    PrepareSource2,

    WaitPrepareSource2,

    /// <summary>落"交互中"标记并发起取片。</summary>
    Pick,

    WaitPick,

    /// <summary>目标站点准备二：开门/放行。</summary>
    PrepareTarget2,

    WaitPrepareTarget2,

    /// <summary>落"交互中"标记并发起放片。</summary>
    Place,

    WaitPlace,
}
