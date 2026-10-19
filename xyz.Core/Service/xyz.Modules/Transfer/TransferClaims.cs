namespace xyz.Modules;

/// <summary>
/// 一趟执行要独占的资源清单
/// </summary>
public sealed class TransferClaims
{
    /// <summary>
    /// 建清单：同一个槽、手、片、站点只记一次
    /// </summary>
    public TransferClaims(IEnumerable<string> slots, IEnumerable<string> arms, IEnumerable<Guid> wafers, IEnumerable<string> stations)
    {
        Slots = slots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Arms = arms.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Wafers = wafers.Distinct().ToList();
        Stations = stations.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 占用的槽（站点#槽号；片在机械手手上按 机械手#手号 记）
    /// </summary>
    public IReadOnlyList<string> Slots { get; }

    /// <summary>
    /// 占用的手（机械手#手号）
    /// </summary>
    public IReadOnlyList<string> Arms { get; }

    /// <summary>
    /// 占用的片（晶圆账内部标识）
    /// </summary>
    public IReadOnlyList<Guid> Wafers { get; }

    /// <summary>
    /// 要跟机械手交互的站点名；同一时刻一个站点只给一趟执行用
    /// </summary>
    public IReadOnlyList<string> Stations { get; }
}
