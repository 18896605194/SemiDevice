using xyz.Components.Enums;
using xyz.Components.Models;
using xyz.Shared.Errors;

namespace xyz.Components.Components;

/// <summary>
/// 一步：几个设备同时发指令（各发各的），都做完算这一步完成。指令发不出去、设备报失败（到位超时、轴报错、PLC 断了）、发过的设备被中止就失败
/// </summary>
internal sealed class DeviceStep : RoutineStep
{
    private readonly IReadOnlyList<DeviceOrder> _orders;

    public DeviceStep(string name, IReadOnlyList<DeviceOrder> orders) : base(name)
    {
        _orders = orders;
    }

    /// <summary>
    /// 还没做完的设备
    /// </summary>
    public override string Waiting
    {
        get
        {
            var running = new List<string>();
            foreach (var order in _orders)
            {
                var device = order.Device;
                if (device.ActionState == ActionState.Running && !running.Contains(device.FullPath))
                {
                    running.Add(device.FullPath);
                }
            }

            return string.Join("、", running);
        }
    }

    protected internal override void Run()
    {
        foreach (var order in _orders)
        {
            if (!order.Send())
            {
                var device = order.Device;
                Fail(ErrorCodes.ChamberDeviceCommandRejected, $"{device.FullPath} {order.Action} 指令没发出去",
                    device.FullPath, order.Action.ToString());
                return;
            }
        }
    }

    protected internal override bool Check()
    {
        bool allDone = true;
        foreach (var order in _orders)
        {
            var device = order.Device;
            switch (device.ActionState)
            {
                case ActionState.Failed:
                    Fail(ErrorCodes.ChamberDeviceActionFailed, $"{device.FullPath} {order.Action} 没做成",
                        device.FullPath, order.Action.ToString());
                    return false;

                case ActionState.Idle:
                    Fail(ErrorCodes.Aborted, $"{device.FullPath} 被中止", Routine.Name);
                    return false;

                case ActionState.Running:
                    allDone = false;
                    break;

                default:
                    break;
            }
        }

        return allDone;
    }
}
