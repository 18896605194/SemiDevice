namespace xyz.Modules;

/// <summary>
/// 搬运管理此刻的占用快照（调度每拍取一份）：哪些槽、手臂、片被搬运单锁着，哪些机械手手上有单（排着或在跑）。
/// 拿到就不变；站点名、机械手名不分大小写。
/// </summary>
public sealed class TransferView
{
    /// <summary>什么都没锁（没装搬运管理时用）。</summary>
    public static TransferView Empty { get; } = new([], [], [], []);

    private readonly HashSet<string> _slots;
    private readonly HashSet<string> _arms;
    private readonly HashSet<Guid> _wafers;
    private readonly HashSet<string> _busyRobots;

    internal TransferView(IEnumerable<string> slots, IEnumerable<string> arms, IEnumerable<Guid> wafers, IEnumerable<string> busyRobots)
    {
        _slots = new HashSet<string>(slots, StringComparer.OrdinalIgnoreCase);
        _arms = new HashSet<string>(arms, StringComparer.OrdinalIgnoreCase);
        _wafers = new HashSet<Guid>(wafers);
        _busyRobots = new HashSet<string>(busyRobots, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>这个槽被搬运单锁着（要从这儿取，或要放到这儿）。</summary>
    public bool IsSlotLocked(string station, int slot)
    {
        return _slots.Contains(TransferManager.SlotKey(station, slot));
    }

    /// <summary>这只手被搬运单占着。</summary>
    public bool IsArmLocked(string robot, int arm)
    {
        return _arms.Contains(TransferManager.ArmKey(robot, arm));
    }

    /// <summary>这一片在某张搬运单里（排着、在搬，或出错留着等人工）。</summary>
    public bool IsWaferLocked(Guid wafer)
    {
        return _wafers.Contains(wafer);
    }

    /// <summary>这台机械手手上有单（排着或在跑）。</summary>
    public bool IsRobotBusy(string robot)
    {
        return _busyRobots.Contains(robot);
    }
}
