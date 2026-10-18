namespace xyz.Components.Models;

/// <summary>
/// 一个站点本轮与机械手交接的信息；取片槽、放片槽都属于接受调用的本站。
/// 每轮创建新对象，后续调用传入同一个实例；同站换槽同时填写两个槽位。
/// </summary>
public sealed class TransferContext
{
    public string Robot { get; }
    public int Arm { get; }
    public int? PickSlot { get; }
    public int? PlaceSlot { get; }

    public TransferContext(string robot, int arm, int? pickSlot, int? placeSlot)
    {
        if (string.IsNullOrWhiteSpace(robot))
        {
            throw new ArgumentException("交接必须指定机械手。", nameof(robot));
        }

        if (arm < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(arm), "手臂编号须从 1 开始。");
        }

        if (pickSlot is null && placeSlot is null)
        {
            throw new ArgumentException("本站交接至少需要一个取片槽或放片槽。");
        }

        Robot = robot;
        Arm = arm;
        PickSlot = pickSlot;
        PlaceSlot = placeSlot;
    }
}
