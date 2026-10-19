namespace xyz.Modules;

/// <summary>
/// 一趟搬运的步骤
/// </summary>
public enum TransferStep
{
    /// <summary>
    /// 抢目标站点
    /// </summary>
    PrepareTarget,

    WaitPrepareTarget,

    PrepareSource,

    WaitPrepareSource,

    PrepareSource2,

    WaitPrepareSource2,

    Pick,

    WaitPick,

    PrepareNextLeg,

    WaitPrepareNextLeg,

    PrepareTarget2,

    WaitPrepareTarget2,

    Place,

    WaitPlace,
}
