namespace xyz.Modules;

/// <summary>
/// 一趟搬运执行里的一片：从哪到哪、哪只手拿着、做到哪一步
/// </summary>
public sealed class TransferWafer
{
    private volatile int _progress;

    internal TransferWafer(string from, int fromSlot, string to, int toSlot, int arm, bool endsOnArm)
    {
        From = from;
        FromSlot = fromSlot;
        To = to;
        ToSlot = toSlot;
        Arm = arm;
        EndsOnArm = endsOnArm;
    }

    /// <summary>
    /// 晶圆账里的内部标识
    /// </summary>
    public Guid Id { get; internal set; }

    /// <summary>
    /// 片号（日志、提示用）
    /// </summary>
    public string Name { get; internal set; } = string.Empty;

    /// <summary>
    /// 归哪个 Job（PJ 名）；手动传片为空
    /// </summary>
    public string? Owner { get; internal set; }

    /// <summary>
    /// 从哪个位置取（站点名，或者拿着它的机械手名）
    /// </summary>
    public string From { get; }

    /// <summary>
    /// 从哪个槽取（机械手上是手号）
    /// </summary>
    public int FromSlot { get; }

    /// <summary>
    /// 放到哪个位置（站点名，或者只取不放时的机械手名）
    /// </summary>
    public string To { get; }

    /// <summary>
    /// 放到哪个槽（机械手上是手号）
    /// </summary>
    public int ToSlot { get; }

    /// <summary>
    /// 这一趟里拿着它的那只手
    /// </summary>
    public int Arm { get; }

    /// <summary>
    /// 这一趟只取不放：做完片停在机械手手上（只取片、同腔交换换出来的那片）
    /// </summary>
    public bool EndsOnArm { get; }

    /// <summary>
    /// 这一片实际做到哪一步；扫描线程写，调度收结果时读
    /// </summary>
    public TransferWaferProgress Progress
    {
        get { return (TransferWaferProgress)_progress; }
        internal set { _progress = (int)value; }
    }
}

/// <summary>
/// 一片在一趟执行里做到哪一步
/// </summary>
public enum TransferWaferProgress
{
    /// <summary>
    /// 还没碰到（片在原位）
    /// </summary>
    None,

    /// <summary>
    /// 取完了，片在机械手手上（账已经记过去）
    /// </summary>
    Picked,

    /// <summary>
    /// 放完了，片在目标位置（账已经记过去）
    /// </summary>
    Placed,
}
