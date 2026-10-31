using xyz._35021.Module.Drivers.Reje;
using xyz._35021.Module.Drivers.Reje.Commands;
using xyz.Elements.Attributes;
using xyz.Elements.Elements;
using xyz.Drivers.Communication;
using xyz.Drivers.Device;
using xyz.Modules;

namespace xyz._35021.Module.Robot;

/// <summary>
/// 35021 机械手模块（锐洁）：动作入口、晶圆账、持片检查、轮询节奏、推送都是平台的（BaseRobotModule），
/// 这里只写每个动作发哪条锐洁指令、一圈查什么、上报怎么认；要加互锁重写 XxxInterlock（先调 base）
/// </summary>
[Element(description: "35021 机械手模块（锐洁）")]
public class RobotModule : BaseRobotModule
{
    /// <summary>
    /// 手指在位推送订上了没有：订阅跟着连接走，断了要重订（只在扫描线程上碰）
    /// </summary>
    private bool _waferEventSubscribed;

    public override IFrameCodec CreateCodec()
    {
        return new RejeCodec();
    }

    #region 动作

    /// <summary>
    /// 回零：@Home
    /// </summary>
    protected override void AddHomeSteps(ModuleRoutine home)
    {
        home.Command("回零", () => new RejeHomeCommand());
    }

    /// <summary>
    /// 取片：@GXYYZZ（持片检查、记账平台在后面加）
    /// </summary>
    protected override void AddPickSteps(ModuleRoutine pick, int arm, int stationNumber, int slot)
    {
        pick.Command("取片", () => new RejePickCommand(arm, stationNumber, slot));
    }

    /// <summary>
    /// 放片：@PXYYZZ
    /// </summary>
    protected override void AddPlaceSteps(ModuleRoutine place, int arm, int stationNumber, int slot)
    {
        place.Command("放片", () => new RejePlaceCommand(arm, stationNumber, slot));
    }

    /// <summary>
    /// 上使能
    /// </summary>
    protected override void AddPowerOnSteps(ModuleRoutine powerOn)
    {
        powerOn.Command("上使能", () => new RejePowerOnCommand());
    }

    /// <summary>
    /// 下使能
    /// </summary>
    protected override void AddPowerOffSteps(ModuleRoutine powerOff)
    {
        powerOff.Command("下使能", () => new RejePowerOffCommand());
    }

    /// <summary>
    /// 清报错
    /// </summary>
    protected override void AddResetSteps(ModuleRoutine reset)
    {
        reset.Command("清报错", () => new RejeResetCommand());
    }

    /// <summary>
    /// 急停：@Sstop
    /// </summary>
    protected override void AddAbortSteps(ModuleRoutine abort)
    {
        abort.Command("急停", () => new RejeSStopCommand());
    }

    #endregion

    #region 状态查询和上报

    /// <summary>
    /// 一圈：没订上先订手指在位推送，再查报错、伺服、速度、每根轴的坐标（按 machine.xml 轴节点的先后）
    /// </summary>
    public override IEnumerable<DeviceCommand> QueryDataLoop()
    {
        // 手指在位推送：machine.xml 开着又还没订上，每圈开头订一次，订上为止
        if (WaferEventEnabled && !_waferEventSubscribed)
        {
            var subscribe = new RejeSubscribeWaferEventCommand();
            yield return subscribe;
            _waferEventSubscribed = subscribe.IsSuccess;
        }

        var error = new RejeQueryErrorCommand();
        yield return error;
        if (error.IsSuccess)
        {
            DeviceError = error.DeviceError;
        }

        var servo = new RejeQueryEnableCommand();
        yield return servo;
        if (servo.IsSuccess)
        {
            IsServoOn = servo.ServoOn;
        }

        var speed = new RejeQuerySpeedCommand();
        yield return speed;
        if (speed.IsSuccess)
        {
            Speed = speed.Speed;
        }

        foreach (var axis in Axes)
        {
            var position = new RejeQueryAxisPosCommand(axis.Name);
            yield return position;
            if (position.IsSuccess)
            {
                axis.UpdatePosition(position.Position);
            }
        }
    }

    /// <summary>
    /// 连接断了：在位推送的订阅跟着连接走，连上以后重订
    /// </summary>
    public override void OnDisconnected()
    {
        _waferEventSubscribed = false;
    }

    /// <summary>
    /// 锐洁的主动推送：手指在位变化、报错（成功码的 Error 帧是迟到的"没有报错"，不算）
    /// </summary>
    public override void OnReport(object message)
    {
        if (message is RejeMessage reje)
        {
            if (RejeProtocol.TryReadWaferEvent(reje, out int arm, out bool hasWafer))
            {
                NoteArmPresence(arm, hasWafer);
                return;
            }

            if (RejeProtocol.IsErrorPush(reje))
            {
                DeviceError = $"{reje.Code}#{reje.Content}";
            }
        }
    }

    #endregion
}
