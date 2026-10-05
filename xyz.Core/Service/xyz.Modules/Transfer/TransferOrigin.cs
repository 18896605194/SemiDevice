namespace xyz.Modules;

/// <summary>
/// 搬运单是谁下的。执行完全一样，区别在受理时查什么、失败后怎么收场。
/// </summary>
public enum TransferOrigin
{
    /// <summary>
    /// 手动传片：人在界面上下的单。碰到被 Job 占着的片一律拒。
    /// </summary>
    Manual,

    /// <summary>
    /// 自动调度：Job 下的单。只能搬自己的片。
    /// </summary>
    Auto,

    /// <summary>
    /// 恢复：出错后人到现场确认过片位，再把片送回去。可以搬被 Job 占着的片。
    /// </summary>
    Recovery,
}
