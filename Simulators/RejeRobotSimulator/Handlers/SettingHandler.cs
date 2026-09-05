using RejeRobotSimulator.Protocol;
using RejeRobotSimulator.State;

namespace RejeRobotSimulator.Handlers;

/// <summary>
/// 设置指令处理器（30条）
/// 修改机器人参数或切换功能状态
/// </summary>
public class SettingHandler : ICommandHandler
{
    private readonly RobotState _state;

    public SettingHandler(RobotState state)
    {
        _state = state;
    }

    public string Handle(ParsedCommand command)
    {
        string cmd = command.CommandName.ToUpper();
        string args = command.Args;

        return cmd switch
        {
            "RESET"           => HandleReset(),
            "PSTOP"           => HandlePStop(),
            "SSTOP"           => HandleSStop(),
            "RESUME"          => HandleResume(),
            "POWERON"         => HandlePowerOn(),
            "POWEROFF"        => HandlePowerOff(),
            "SPEED"           => HandleSpeed(args),
            "ARMDISTANCE"     => HandleArmDistance(args),
            "UNLOAD"          => HandleUnload(),
            "OPENEMV"         => HandleOpenEMV(args),
            "CLOSEEMV"        => HandleCloseEMV(args),
            "OPENSTARTHEART"  => HandleOpenStartHeart(),
            "CLOSESTARTHEART" => HandleCloseStartHeart(),
            "QHT"             => HandleQHT(),
            "HEARTTIME"       => HandleHeartTime(args),
            "AXISWORKHOME"    => HandleAxisWorkHome(args, command.RawCommand),
            "SETAXISPOS"      => HandleSetAxisPos(args, command.RawCommand),
            "SETAXISNEG"      => HandleSetAxisNeg(args, command.RawCommand),
            "SETAXISV"        => HandleSetAxisV(args, command.RawCommand),
            "DMO"             => HandleDMO(),
            "DMC"             => HandleDMC(),
            "AEO"             => HandleAEO(),
            "AEC"             => HandleAEC(),
            "SWO"             => HandleSWO(),
            "SWC"             => HandleSWC(),
            "AXISRANGE"       => HandleAxisRange(args, command.RawCommand),
            "ZLS"             => HandleZLS(args),
            "ZFS"             => HandleZFS(args),
            "SAWCD"           => HandleSAWCD(args),
            "SUBWAFER"        => HandleSubWafer(args),
            _ => ResponseBuilder.BuildFailure(command.CommandName, "99990002", "Unknown setting command")
        };
    }

    // --- 基础控制 ---

    private string HandleReset()
    {
        _state.Reset();
        return ResponseBuilder.BuildSuccess("Reset");
    }

    private string HandlePStop()
    {
        // 平稳停止: 打断进行中的运动等待 (运动指令按"被打断"回错误帧)
        _state.AbortSignal.Set();
        _state.IsExecuting = false;
        return ResponseBuilder.BuildSuccess("PStop");
    }

    private string HandleSStop()
    {
        // 急停: 打断进行中的运动等待 (运动指令按"被打断"回错误帧)
        _state.AbortSignal.Set();
        _state.IsExecuting = false;
        return ResponseBuilder.BuildSuccess("SStop");
    }

    private string HandleResume()
    {
        if (_state.OpMode != 2)
            return ResponseBuilder.BuildFailure("Resume", "99990010", "Resume only works in auto mode");
        _state.IsExecuting = true;
        return ResponseBuilder.BuildSuccess("Resume");
    }

    private string HandlePowerOn()
    {
        // 协议：PowerOn 只有在自动模式下才有效（手动模式靠 DeadMan 上使能）
        if (_state.OpMode != 2)
            return ResponseBuilder.BuildFailure("PowerOn", "99990010", "PowerOn only works in auto mode");
        _state.IsEnabled = true;
        return ResponseBuilder.BuildSuccess("PowerOn");
    }

    private string HandlePowerOff()
    {
        if (_state.OpMode != 2)
            return ResponseBuilder.BuildFailure("PowerOff", "99990010", "PowerOff only works in auto mode");
        _state.IsEnabled = false;
        return ResponseBuilder.BuildSuccess("PowerOff");
    }

    // --- 速度 ---

    private string HandleSpeed(string args)
    {
        if (!int.TryParse(args, out int speed) || speed < 1 || speed > 100)
            return ResponseBuilder.BuildFailure("Speed", "99990003", "Speed must be 1-100");
        _state.SpeedPercent = speed;
        return ResponseBuilder.BuildSuccess("Speed" + speed);
    }

    // --- Arm 间距 ---

    private string HandleArmDistance(string args)
    {
        if (!double.TryParse(args, out double dist))
            return ResponseBuilder.BuildFailure("ArmDistance", "99990003", "Invalid distance value");
        _state.ArmDistance = dist;
        return ResponseBuilder.BuildSuccess("ArmDistance" + args);
    }

    // --- 卸载 ---

    private string HandleUnload()
    {
        _state.ProjectName = "";
        _state.ProgramName = "";
        return ResponseBuilder.BuildSuccess("Unload");
    }

    // --- 真空 ---

    private string HandleOpenEMV(string args)
    {
        switch (args)
        {
            case "1": _state.EMV1 = true; break;
            case "2": _state.EMV2 = true; break;
            case "3": _state.EMV3 = true; break;
            case "4": _state.EMV4 = true; break;
            default: return ResponseBuilder.BuildFailure("OpenEMV", "99990003", "EMV channel must be 1-4");
        }
        return ResponseBuilder.BuildSuccess("OpenEMV " + args);
    }

    private string HandleCloseEMV(string args)
    {
        switch (args)
        {
            case "1": _state.EMV1 = false; break;
            case "2": _state.EMV2 = false; break;
            case "3": _state.EMV3 = false; break;
            case "4": _state.EMV4 = false; break;
            default: return ResponseBuilder.BuildFailure("CloseEMV", "99990003", "EMV channel must be 1-4");
        }
        return ResponseBuilder.BuildSuccess("CloseEMV" + args);
    }

    // --- 心跳 ---

    private string HandleOpenStartHeart()
    {
        _state.HeartbeatEnabled = true;
        return ResponseBuilder.BuildSuccess("OpenStartHeart");
    }

    private string HandleCloseStartHeart()
    {
        _state.HeartbeatEnabled = false;
        return ResponseBuilder.BuildSuccess("CloseStartHeart");
    }

    private string HandleQHT()
    {
        return ResponseBuilder.BuildSuccess("QHT", _state.HeartbeatIntervalMs.ToString());
    }

    private string HandleHeartTime(string args)
    {
        if (!int.TryParse(args, out int ms) || ms <= 0)
            return ResponseBuilder.BuildFailure("HeartTime", "99990003", "Invalid heartbeat interval");
        _state.HeartbeatIntervalMs = ms;
        return ResponseBuilder.BuildSuccess("HeartTime" + args);
    }

    // --- 轴参数 ---

    private string HandleAxisWorkHome(string args, string echo)
    {
        // 格式: Axis + 数值，如 Z10, X0, Theta45
        var (axis, value) = SplitAxisArgs(args);
        if (axis == null || !double.TryParse(value, out double v))
            return ResponseBuilder.BuildFailure(echo, "99990003", "Invalid args");

        switch (axis.ToUpper())
        {
            case "X": _state.WorkHomeX = v; break;
            case "Z": _state.WorkHomeZ = v; break;
            case "THETA": _state.WorkHomeTheta = v; break;
            case "ARM1": _state.WorkHomeArm1 = v; break;
            case "ARM2": _state.WorkHomeArm2 = v; break;
            case "ARM3": _state.WorkHomeArm3 = v; break;
            case "ARM4": _state.WorkHomeArm4 = v; break;
            case "AUX": _state.WorkHomeAux = v; break;
            default: return ResponseBuilder.BuildFailure(echo, "99990003", "Unknown axis");
        }
        return ResponseBuilder.BuildSuccess(echo);
    }

    private string HandleSetAxisPos(string args, string echo)
    {
        var (axis, value) = SplitAxisArgs(args);
        if (axis == null || !double.TryParse(value, out double v))
            return ResponseBuilder.BuildFailure(echo, "99990003", "Invalid args");

        switch (axis.ToUpper())
        {
            case "X": _state.PosLimitX = v; break;
            case "Z": _state.PosLimitZ = v; break;
            case "THETA": _state.PosLimitTheta = v; break;
            case "ARM1": _state.PosLimitArm1 = v; break;
            case "ARM2": _state.PosLimitArm2 = v; break;
            case "ARM3": _state.PosLimitArm3 = v; break;
            case "ARM4": _state.PosLimitArm4 = v; break;
            case "AUX": _state.PosLimitAux = v; break;
            default: return ResponseBuilder.BuildFailure(echo, "99990003", "Unknown axis");
        }
        return ResponseBuilder.BuildSuccess(echo);
    }

    private string HandleSetAxisNeg(string args, string echo)
    {
        var (axis, value) = SplitAxisArgs(args);
        if (axis == null || !double.TryParse(value, out double v))
            return ResponseBuilder.BuildFailure(echo, "99990003", "Invalid args");

        switch (axis.ToUpper())
        {
            case "X": _state.NegLimitX = v; break;
            case "Z": _state.NegLimitZ = v; break;
            case "THETA": _state.NegLimitTheta = v; break;
            case "ARM1": _state.NegLimitArm1 = v; break;
            case "ARM2": _state.NegLimitArm2 = v; break;
            case "ARM3": _state.NegLimitArm3 = v; break;
            case "ARM4": _state.NegLimitArm4 = v; break;
            case "AUX": _state.NegLimitAux = v; break;
            default: return ResponseBuilder.BuildFailure(echo, "99990003", "Unknown axis");
        }
        return ResponseBuilder.BuildSuccess(echo);
    }

    private string HandleSetAxisV(string args, string echo)
    {
        var (axis, value) = SplitAxisArgs(args);
        if (axis == null || !double.TryParse(value, out double v))
            return ResponseBuilder.BuildFailure(echo, "99990003", "Invalid args");

        switch (axis.ToUpper())
        {
            case "X": _state.MaxVelX = v; break;
            case "Z": _state.MaxVelZ = v; break;
            case "THETA": _state.MaxVelTheta = v; break;
            case "ARM1": _state.MaxVelArm1 = v; break;
            case "ARM2": _state.MaxVelArm2 = v; break;
            case "ARM3": _state.MaxVelArm3 = v; break;
            case "ARM4": _state.MaxVelArm4 = v; break;
            case "AUX": _state.MaxVelAux = v; break;
            default: return ResponseBuilder.BuildFailure(echo, "99990003", "Unknown axis");
        }
        return ResponseBuilder.BuildSuccess(echo);
    }

    // --- 功能开关 ---

    private string HandleDMO()
    {
        _state.DeadManErrorEnabled = true;
        return ResponseBuilder.BuildSuccess("DMO");
    }

    private string HandleDMC()
    {
        _state.DeadManErrorEnabled = false;
        return ResponseBuilder.BuildSuccess("DMC");
    }

    private string HandleAEO()
    {
        _state.ActiveErrorEnabled = true;
        return ResponseBuilder.BuildSuccess("AEO");
    }

    private string HandleAEC()
    {
        _state.ActiveErrorEnabled = false;
        return ResponseBuilder.BuildSuccess("AEC");
    }

    private string HandleSWO()
    {
        _state.SlideDetectEnabled = true;
        return ResponseBuilder.BuildSuccess("SWO");
    }

    private string HandleSWC()
    {
        _state.SlideDetectEnabled = false;
        return ResponseBuilder.BuildSuccess("SWC");
    }

    // --- 轴范围 / 速度比率 ---

    private string HandleAxisRange(string args, string echo)
    {
        var (axis, value) = SplitAxisArgs(args);
        if (axis == null || !double.TryParse(value, out double v))
            return ResponseBuilder.BuildFailure(echo, "99990003", "Invalid args");

        switch (axis.ToUpper())
        {
            case "Z": _state.AxisRangeZ = v; break;
            case "R": _state.AxisRangeR = v; break;
            default: return ResponseBuilder.BuildFailure(echo, "99990003", "Axis must be Z or R");
        }
        return ResponseBuilder.BuildSuccess(echo);
    }

    private string HandleZLS(string args)
    {
        if (!int.TryParse(args, out int ratio) || ratio < 1 || ratio > 100)
            return ResponseBuilder.BuildFailure("ZLS", "99990003", "ZLS must be 1-100");
        _state.ZLoadSpeedRatio = ratio;
        return ResponseBuilder.BuildSuccess("ZLS" + args);
    }

    private string HandleZFS(string args)
    {
        if (!int.TryParse(args, out int ratio) || ratio < 1 || ratio > 100)
            return ResponseBuilder.BuildFailure("ZFS", "99990003", "ZFS must be 1-100");
        _state.ZFetchSpeedRatio = ratio;
        return ResponseBuilder.BuildSuccess("ZFS" + args);
    }

    // --- AWC ---

    private string HandleSAWCD(string args)
    {
        string[] parts = args.Split(',');
        if (parts.Length != 2 ||
            !double.TryParse(parts[0], out double maxX) ||
            !double.TryParse(parts[1], out double maxY))
            return ResponseBuilder.BuildFailure("SAWCD", "99990003", "SAWCD format: XX,YY");

        _state.AwcMaxX = maxX;
        _state.AwcMaxY = maxY;
        return ResponseBuilder.BuildSuccess("SAWCD" + args);
    }

    // --- Wafer 订阅 ---

    private string HandleSubWafer(string args)
    {
        if (args == "1")
        {
            // 订阅置位后 TcpServer.ProcessCommand 会立即补推 4 臂在位基线 (BroadcastCurrentWaferStates)
            _state.WaferSubscribed = true;
            return ResponseBuilder.BuildSuccess("SubWafer1");
        }
        else if (args == "0")
        {
            _state.WaferSubscribed = false;
            return ResponseBuilder.BuildSuccess("SubWafer0");
        }
        return ResponseBuilder.BuildFailure("SubWafer", "99990003", "SubWafer arg must be 0 or 1");
    }

    // --- 辅助方法 ---

    /// <summary>
    /// 分离轴名和数值，如 "Z10" -> ("Z","10"), "Arm3-5" -> ("Arm3","-5")。
    /// 委托 CommandParser.SplitAxisValue 按已知轴名整体匹配，正确处理 Arm1~Arm4 等带数字轴名。
    /// </summary>
    private static (string? axis, string value) SplitAxisArgs(string args)
    {
        return CommandParser.SplitAxisValue(args);
    }
}
