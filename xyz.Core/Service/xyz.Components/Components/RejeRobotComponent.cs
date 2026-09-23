using xyz.Components.Attributes;
using xyz.Drivers.Communication;
using xyz.Drivers.Robot;
using xyz.Drivers.Robot.Reje;
using xyz.Drivers.Robot.Reje.Commands;

namespace xyz.Components.Components;

/// <summary>
/// 锐洁机械手驱动组件：网口 + 锐洁帧编解码 + RejeRobotDriver，每个动作对应一条锐洁指令。
/// Reje/*Command 在壳内消化厂商怪癖（Wire/回显认领），上层只读 command.Response。
/// </summary>
[Component(description: "锐洁机械手驱动组件")]
public class RejeRobotComponent : RobotDriverComponent
{
    protected override IRobotDriver CreateDriver()
    {
        var transport = CommunicationFactory.CreateTcp(Host, NetPort);
        return new RejeRobotDriver(new FrameCommunication(transport, new RejeFrameCodec()));
    }

    protected override RobotCommand CreateHome()
    {
        return new RejeHomeCommand(Driver!);
    }

    protected override RobotCommand CreatePick(int arm, int station, int slot)
    {
        return new RejePickCommand(Driver!, arm, station, slot);
    }

    protected override RobotCommand CreatePlace(int arm, int station, int slot)
    {
        return new RejePlaceCommand(Driver!, arm, station, slot);
    }

    protected override RobotCommand CreatePowerOn()
    {
        return new RejePowerOnCommand(Driver!);
    }

    protected override RobotCommand CreatePowerOff()
    {
        return new RejePowerOffCommand(Driver!);
    }

    protected override RobotCommand CreateStop()
    {
        return new RejeSStopCommand(Driver!);
    }

    protected override RobotCommand CreateResetDrive()
    {
        return new RejeResetCommand(Driver!);
    }

    protected override RobotCommand CreateQueryAxisPos(string axis)
    {
        return new RejeQueryAxisPosCommand(Driver!, axis);
    }

    protected override RobotCommand CreateQueryDeviceError()
    {
        return new RejeQueryErrorCommand(Driver!);
    }

    protected override RobotCommand CreateQueryServoOn()
    {
        return new RejeQueryEnableCommand(Driver!);
    }

    protected override RobotCommand CreateSubscribeWaferEvent()
    {
        return new RejeSubscribeWaferEventCommand(Driver!);
    }
}
