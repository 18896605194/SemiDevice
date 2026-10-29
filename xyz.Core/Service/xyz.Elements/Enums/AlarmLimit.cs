namespace xyz.Elements.Enums;

/// <summary>
/// 报警挂着期间的限制，可以几样一起
/// </summary>
[Flags]
public enum AlarmLimit
{
    /// <summary>
    /// 没有限制
    /// </summary>
    None = 0,

    /// <summary>
    /// 不投新片：腔体不再放新片进来，整机不再从载具取新片，机台里的片照常走完
    /// </summary>
    NoNewWafer = 1,

    /// <summary>
    /// 锁安全阀：配了安全位的阀锁在安全位，谁来写都拒
    /// </summary>
    LockOutputs = 2,

    /// <summary>
    /// 挡开工：不能切 Auto、全部初始化、开 Job
    /// </summary>
    BlockStart = 4
}
