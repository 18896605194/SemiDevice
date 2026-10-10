using xyz.Components.Components;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 腔体回零（平台默认做法，照 sc 里挂的设备走）：喷嘴全关 → 卡盘停转 → Lift 升 → 摆臂回零 → Bowl 降，门不动（门归站点交互环管）。
/// 每一段同时发、等都做完再走下一段；Lift 先升起来，摆臂回零时才不会刮到 Bowl 壁。
/// sc 里没配的设备那一段就空着过去；除卡盘以外别的轴跟摆臂一起回零。机型的先后不一样就重写腔体的 Home。
/// </summary>
internal sealed class ChamberHomeOperation : ChamberStepOperation
{
    private readonly BaseChamberModule _chamber;
    private int _stage;

    public ChamberHomeOperation(BaseChamberModule chamber, int timeout)
        : base($"{chamber.Name} Home", chamber.Name, timeout)
    {
        _chamber = chamber;
    }

    protected override void Advance()
    {
        int stage = _stage++;
        switch (stage)
        {
            case 0:
                foreach (var nozzle in _chamber.Nozzles)
                {
                    if (!Send(nozzle, ChamberDeviceAction.ValveOff, nozzle.Stop))
                    {
                        return;
                    }
                }

                break;

            case 1:
                var spin = _chamber.SpinMotor;
                if (spin is not null)
                {
                    Send(spin, ChamberDeviceAction.Stop, spin.Stop);
                }

                break;

            case 2:
                foreach (var arm in _chamber.Arms)
                {
                    var lift = arm.Lift;
                    if (lift is not null && !Send(lift, ChamberDeviceAction.Up, lift.Open))
                    {
                        return;
                    }
                }

                break;

            case 3:
                foreach (var axis in _chamber.Axes)
                {
                    if (axis is not SpinMotorComponent && !Send(axis, ChamberDeviceAction.Home, axis.Home))
                    {
                        return;
                    }
                }

                break;

            case 4:
                foreach (var bowl in _chamber.Bowls)
                {
                    if (!Send(bowl, ChamberDeviceAction.Down, bowl.Close))
                    {
                        return;
                    }
                }

                break;

            default:
                Complete();
                break;
        }
    }
}
