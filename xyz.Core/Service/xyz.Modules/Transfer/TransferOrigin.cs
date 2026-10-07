namespace xyz.Modules;

/// <summary>
/// 搬运操作的调用来源，用于校验晶圆归属；实际执行流程相同。
/// </summary>
public enum TransferOrigin
{
    /// <summary>
    /// 手动传片：界面调用。碰到被 Job 占着的片一律拒。
    /// </summary>
    Manual,

    /// <summary>
    /// 自动调度：执行 Job 的取放任务。只能搬自己的片。
    /// </summary>
    Auto,

    /// <summary>
    /// 恢复：出错后人到现场确认过片位，再把片送回去。可以搬被 Job 占着的片。
    /// </summary>
    Recovery,
}
