using RejeRobotSimulator.Protocol;
using RejeRobotSimulator.State;

namespace RejeRobotSimulator.Handlers;

/// <summary>
/// 查询指令处理器（14条）
/// 不改变机器人状态，只返回当前数据
/// </summary>
public class QueryHandler : ICommandHandler
{
    private readonly RobotState _state;

    public QueryHandler(RobotState state)
    {
        _state = state;
    }

    public string Handle(ParsedCommand command)
    {
        string cmd = command.CommandName.ToUpper();
        return cmd switch
        {
            "STATUS"     => HandleStatus(),
            "ERROR"      => HandleError(),
            "PROJECT"    => HandleProject(),
            "PROGRAM"    => HandleProgram(),
            "PRESSURE"   => HandlePressure(),
            "ACTIVE"     => HandleActive(),
            "QENABLE"    => HandleQEnable(),
            "AXISPOS"    => HandleAxisPos(command),
            "QSPEED"     => HandleQSpeed(),
            "DRIVEERROR" => HandleDriveError(),
            "QOPMODE"    => HandleQOpMode(),
            "QAWC"       => HandleQAWC(),
            "QAWCD"      => HandleQAWCD(),
            "QSUBWAFER"  => HandleQSubWafer(),
            _ => ResponseBuilder.BuildFailure(command.CommandName, "99990001", "Unknown query command")
        };
    }

    /// <summary>@Status; 查询Robot当前状态</summary>
    private string HandleStatus()
    {
        string content = _state.HasError
            ? "The robot has error now"
            : "The robot is OK";
        return ResponseBuilder.BuildSuccess("Status", content);
    }

    /// <summary>@Error; 查询当前报错内容</summary>
    private string HandleError()
    {
        if (_state.HasError)
            return ResponseBuilder.BuildFailure("Error", _state.ErrorCode, _state.ErrorDesc);
        return ResponseBuilder.BuildSuccess("Error", "The robot is OK now");
    }

    /// <summary>@Project; 查询当前挂载项目</summary>
    private string HandleProject()
    {
        return ResponseBuilder.BuildSuccess("Project", _state.ProjectName);
    }

    /// <summary>@Program; 查询当前挂载程序</summary>
    private string HandleProgram()
    {
        return ResponseBuilder.BuildSuccess("Program", _state.ProgramName);
    }

    /// <summary>@Pressure; 查询压力表状态（4 臂）</summary>
    private string HandlePressure()
    {
        string arm1 = _state.Arm1Pressure ? "1YES" : "1NO";
        string arm2 = _state.Arm2Pressure ? "2YES" : "2NO";
        string arm3 = _state.Arm3Pressure ? "3YES" : "3NO";
        string arm4 = _state.Arm4Pressure ? "4YES" : "4NO";
        return ResponseBuilder.BuildSuccess("Pressure", $"{arm1},{arm2},{arm3},{arm4}");
    }

    /// <summary>@Active; 查询是否正在执行程序</summary>
    private string HandleActive()
    {
        return ResponseBuilder.BuildSuccess("Active", _state.IsExecuting ? "YES" : "NO");
    }

    /// <summary>@QEnable; 查询伺服是否上使能</summary>
    private string HandleQEnable()
    {
        return ResponseBuilder.BuildSuccess("QEnable", _state.IsEnabled ? "YES" : "NO");
    }

    /// <summary>@&lt;轴&gt;Pos; 查询各轴位置，回显原始指令名（如 @XPos）</summary>
    private string HandleAxisPos(ParsedCommand command)
    {
        string axis = command.Args.ToUpper().Trim();
        double pos = axis switch
        {
            "X"      => _state.PosX,
            "Z"      => _state.PosZ,
            "THETA"  => _state.PosTheta,
            "ARM1"   => _state.PosArm1,
            "ARM2"   => _state.PosArm2,
            "ARM3"   => _state.PosArm3,
            "ARM4"   => _state.PosArm4,
            "AUX"    => _state.PosAux,
            "FLIP1"  => _state.PosFlip1,
            "FLIP2"  => _state.PosFlip2,
            _        => 0
        };
        return ResponseBuilder.BuildSuccess(command.RawCommand, pos.ToString("F3"));
    }

    /// <summary>@QSpeed; 查询全局速度百分比</summary>
    private string HandleQSpeed()
    {
        return ResponseBuilder.BuildSuccess("QSpeed", _state.SpeedPercent.ToString());
    }

    /// <summary>@DriveError; 查询驱动器错误</summary>
    private string HandleDriveError()
    {
        if (_state.DriveErrorCode != "00000000")
            return ResponseBuilder.BuildFailure("DriveError", _state.DriveErrorCode, _state.DriveErrorDesc);
        return ResponseBuilder.BuildSuccess("DriveError", "No error");
    }

    /// <summary>@QOpMode; 查询操作模式</summary>
    private string HandleQOpMode()
    {
        return ResponseBuilder.BuildSuccess("QOpMode", _state.OpMode.ToString());
    }

    /// <summary>@QAWC; 查询AWC补偿数据</summary>
    private string HandleQAWC()
    {
        string content = $"{_state.AwcStation},{_state.AwcSlot},{_state.AwcOffsetX},{_state.AwcOffsetY}";
        return ResponseBuilder.BuildSuccess("QAWC", content);
    }

    /// <summary>@QAWCD; 查询AWC最大纠偏范围</summary>
    private string HandleQAWCD()
    {
        string content = $"{_state.AwcMaxX},{_state.AwcMaxY}";
        return ResponseBuilder.BuildSuccess("QAWCD", content);
    }

    /// <summary>@QSubWafer; 查询Wafer订阅状态</summary>
    private string HandleQSubWafer()
    {
        return ResponseBuilder.BuildSuccess("QSubWafer", _state.WaferSubscribed ? "1" : "0");
    }
}
