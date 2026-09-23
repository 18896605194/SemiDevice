using System.Globalization;
using xyz.Drivers.Communication;
using xyz.Drivers.Robot.Reje.Commands;

namespace xyz.Drivers.Robot.Reje;

/// <summary>
/// 锐洁机械手驱动：指令受理与帧路由机制继承自 RobotDriverBase，本类只补锐洁特有部分——
/// 无主推送帧的归一化（手指在位、主动报错、心跳），以及急停确认后打断在途运动指令（设备不再回运动结果）。
/// </summary>
public class RejeRobotDriver : RobotDriverBase
{
    public RejeRobotDriver(IFrameCommunication communication) : base(communication)
    {
    }

    /// <summary>
    /// 无主帧 → 厂商无关主动事件。确认帧（"&gt;"）不带回显名、只表示受理，这里忽略。
    /// </summary>
    protected override RobotDeviceEvent? ParseSpontaneousEvent(string body)
    {
        if (!RejeProtocol.TrySplit(body, out string code, out string content, out string name))
        {
            return null;
        }

        if (string.Equals(name, RejeProtocol.EventName, StringComparison.OrdinalIgnoreCase))
        {
            return ParseWaferEvent(content, body);
        }

        if (string.Equals(name, RejeProtocol.ErrorName, StringComparison.OrdinalIgnoreCase))
        {
            return new RobotDeviceEvent
            {
                Kind = RobotDeviceEventKind.DeviceError,
                Content = $"{code}#{content}",
                RawFrame = body,
                ReceivedAt = DateTime.Now,
            };
        }

        if (string.Equals(name, RejeProtocol.HeartBeatName, StringComparison.OrdinalIgnoreCase))
        {
            return new RobotDeviceEvent
            {
                Kind = RobotDeviceEventKind.HeartBeat,
                Content = content,
                RawFrame = body,
                ReceivedAt = DateTime.Now,
            };
        }

        return null;
    }

    /// <summary>
    /// SubWaferEx,&lt;手指&gt;,&lt;0有片|1无片&gt;：注意 0 表示有片。
    /// </summary>
    private static RobotDeviceEvent? ParseWaferEvent(string content, string body)
    {
        string[] parts = content.Split(',');
        if (parts.Length != 3
            || !string.Equals(parts[0].Trim(), RejeProtocol.WaferEventName, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int arm))
        {
            return null;
        }

        string flag = parts[2].Trim();
        if (flag != "0" && flag != "1")
        {
            return null;
        }

        return new RobotDeviceEvent
        {
            Kind = RobotDeviceEventKind.WaferPresence,
            Arm = arm,
            HasWafer = flag == "0",
            Content = content,
            RawFrame = body,
            ReceivedAt = DateTime.Now,
        };
    }

    /// <summary>
    /// 急停确认后设备不再回被打断的运动结果：主动以失败终结在途运动指令，腾出同名槽位。
    /// </summary>
    protected override void OnCommandCompleted(RobotCommand command)
    {
        if (command is RejeSStopCommand && command.Response is { IsSuccess: true })
        {
            InterruptMotions("被急停打断（Sstop）");
        }
    }
}
