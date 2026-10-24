using xyz.Components.Interfaces;
using xyz.Shared.Dtos;

namespace xyz.Components.Components;

/// <summary>
/// 给设备发一个不带参数的动作（升 / 降、开关阀、回零 / 停 / 复位），整腔动作的设备步骤和手动页共用
/// </summary>
public static class DeviceActions
{
    /// <summary>
    /// 发出去返回 true；动作跟设备对不上、指令发不出去返回 false
    /// </summary>
    public static bool Send(IDevice device, ChamberDeviceAction action)
    {
        switch (action)
        {
            case ChamberDeviceAction.Up:
                if (device is ICylinder raising)
                {
                    return raising.Open();
                }

                return false;

            case ChamberDeviceAction.Down:
                if (device is ICylinder lowering)
                {
                    return lowering.Close();
                }

                return false;

            case ChamberDeviceAction.ValveOn:
                if (device is IValve opening)
                {
                    return opening.Open();
                }

                return false;

            case ChamberDeviceAction.ValveOff:
                // 喷嘴关阀走 Stop：machine.xml 配了的还把流量清零
                if (device is INozzle nozzle)
                {
                    return nozzle.Stop();
                }

                if (device is IValve closing)
                {
                    return closing.Close();
                }

                return false;

            case ChamberDeviceAction.Home:
                if (device is IAxis homing)
                {
                    return homing.Home();
                }

                return false;

            case ChamberDeviceAction.Stop:
                if (device is IAxis stopping)
                {
                    return stopping.Stop();
                }

                return false;

            case ChamberDeviceAction.Reset:
                if (device is IAxis resetting)
                {
                    return resetting.ResetDrive();
                }

                return false;

            default:
                return false;
        }
    }
}
