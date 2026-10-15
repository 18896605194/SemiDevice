using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 取放准备二（平台默认做法，照 sc 里挂的设备走）：卡盘转到取放片示教位（卡盘 EC TransferPosition）、开腔门，
/// 两样同时发、都到位才算准备好，机械手才伸手进来。卡盘转位和开门互不碰，同时做省时间。
/// 卡盘先发：没回零（绝对定位发不出去）就直接失败，门不开。sc 里没配卡盘或门的那一样就不动。
/// </summary>
internal sealed class ChamberPrepareTransferOperation : ChamberStepOperation
{
    private readonly BaseChamberModule _chamber;
    private int _stage;

    public ChamberPrepareTransferOperation(BaseChamberModule chamber, int timeout)
        : base($"{chamber.Name} PrepareTransfer2", chamber.Name, timeout)
    {
        _chamber = chamber;
    }

    protected override void Advance()
    {
        int stage = _stage++;
        switch (stage)
        {
            case 0:
                var spin = _chamber.SpinMotor;
                if (spin is not null && !Send(spin, ChamberDeviceAction.Move, () => spin.MoveTo(spin.TransferPosition)))
                {
                    return;
                }

                var door = _chamber.Door;
                if (door is not null)
                {
                    Send(door, ChamberDeviceAction.Up, door.Open);
                }

                break;

            default:
                Complete();
                break;
        }
    }
}
