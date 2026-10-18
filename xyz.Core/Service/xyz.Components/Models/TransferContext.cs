namespace xyz.Components.Models;

/// <summary>
/// 一个站点本轮跟机械手交接的信息：取一段、放一段，或者先取后放；每轮新建一个，站点按对象认本轮
/// </summary>
public sealed class TransferContext
{
    /// <summary>
    /// 哪台机械手
    /// </summary>
    public string Robot { get; }

    /// <summary>
    /// 取片用的手；这一轮不取片为 null
    /// </summary>
    public int? PickArm { get; }

    /// <summary>
    /// 从本站哪个槽取片；这一轮不取片为 null
    /// </summary>
    public int? PickSlot { get; }

    /// <summary>
    /// 放片用的手；这一轮不放片为 null
    /// </summary>
    public int? PlaceArm { get; }

    /// <summary>
    /// 放到本站哪个槽；这一轮不放片为 null
    /// </summary>
    public int? PlaceSlot { get; }

    /// <summary>
    /// 这一轮要取片
    /// </summary>
    public bool HasPick => PickSlot is not null;

    /// <summary>
    /// 这一轮要放片
    /// </summary>
    public bool HasPlace => PlaceSlot is not null;

    private TransferContext(string robot, int? pickArm, int? pickSlot, int? placeArm, int? placeSlot)
    {
        if (string.IsNullOrWhiteSpace(robot))
        {
            throw new ArgumentException("交接必须指定机械手。", nameof(robot));
        }

        Robot = robot;
        PickArm = pickArm;
        PickSlot = pickSlot;
        PlaceArm = placeArm;
        PlaceSlot = placeSlot;
    }

    /// <summary>
    /// 只取片：机械手用这只手从本站这个槽取走
    /// </summary>
    public static TransferContext ForPick(string robot, int arm, int slot)
    {
        CheckNumber(arm, nameof(arm));
        CheckNumber(slot, nameof(slot));
        return new TransferContext(robot, arm, slot, null, null);
    }

    /// <summary>
    /// 只放片：机械手把这只手上的片放到本站这个槽
    /// </summary>
    public static TransferContext ForPlace(string robot, int arm, int slot)
    {
        CheckNumber(arm, nameof(arm));
        CheckNumber(slot, nameof(slot));
        return new TransferContext(robot, null, null, arm, slot);
    }

    /// <summary>
    /// 一轮先取后放：同站换槽两只手相同，同腔交换是两只不同的手
    /// </summary>
    public static TransferContext ForPickThenPlace(string robot, int pickArm, int pickSlot, int placeArm, int placeSlot)
    {
        CheckNumber(pickArm, nameof(pickArm));
        CheckNumber(pickSlot, nameof(pickSlot));
        CheckNumber(placeArm, nameof(placeArm));
        CheckNumber(placeSlot, nameof(placeSlot));
        return new TransferContext(robot, pickArm, pickSlot, placeArm, placeSlot);
    }

    /// <summary>
    /// 手号、槽号都从 1 开始；写错是代码问题，直接抛
    /// </summary>
    private static void CheckNumber(int value, string name)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(name, "手号、槽号都从 1 开始。");
        }
    }
}
