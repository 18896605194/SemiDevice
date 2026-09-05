using RejeRobotSimulator.Protocol;
using RejeRobotSimulator.State;

namespace RejeRobotSimulator.Handlers;

/// <summary>
/// 动作指令处理器（12条）
/// 模拟 Robot 取片/放片/回原点等动作
/// </summary>
public class MotionHandler : ICommandHandler
{
    private readonly RobotState _state;
    private readonly Random _random = new();

    /// <summary>运动占用判定与置位的互斥锁 (见 TryBeginMotion)。</summary>
    private readonly object _motionLock = new();

    /// <summary>仿真动作延迟（毫秒），模拟真实机械动作耗时</summary>
    public int SimulatedDelayMs { get; set; } = 3000;

    /// <summary>仿真失败概率（0-100），0=永不失败</summary>
    public int FailureRate { get; set; } = 0;

    public MotionHandler(RobotState state)
    {
        _state = state;
    }

    public string Handle(ParsedCommand command)
    {
        string cmd = command.CommandName.ToUpper();
        string args = command.Args;

        // 动作指令需要使能状态
        if (!_state.IsEnabled)
            return ResponseBuilder.BuildFailure(command.CommandName, "99990020", "Robot not enabled");

        return cmd switch
        {
            "HOME"   => HandleHome(args, command.RawCommand),
            "G"      => HandleGet(args, "G"),
            "GIN"    => HandleGet(args, "GIN"),
            "GOT"    => HandleGet(args, "GOT"),
            "GW"     => HandleGet(args, "GW"),
            "GWA"    => HandleGet(args, "GWa"),
            "P"      => HandlePlace(args, "P"),
            "PIN"    => HandlePlace(args, "PIN"),
            "POT"    => HandlePlace(args, "POT"),
            "PW"     => HandlePlace(args, "PW"),
            "PWA"    => HandlePlace(args, "PWa"),
            "GAP"    => HandleGap(args),
            _ => ResponseBuilder.BuildFailure(command.CommandName, "99990004", "Unknown motion command")
        };
    }

    /// <summary>
    /// @Home; 回原点
    /// </summary>
    private string HandleHome(string args, string echo)
    {
        if (TryBeginMotion(echo, SimulatedDelayMs, out string rejected))
            return rejected;

        if (ShouldSimulateFailure())
        {
            _state.IsExecuting = false;
            string errCode = GenerateRandomErrorCode();
            return ResponseBuilder.BuildFailure(echo, errCode, "Home failed (simulated)");
        }

        // 重置各轴到原点
        if (string.IsNullOrEmpty(args) || args.ToUpper() == "ALL")
        {
            _state.PosX = _state.WorkHomeX;
            _state.PosZ = _state.WorkHomeZ;
            _state.PosTheta = _state.WorkHomeTheta;
            _state.PosArm1 = _state.WorkHomeArm1;
            _state.PosArm2 = _state.WorkHomeArm2;
            _state.PosArm3 = _state.WorkHomeArm3;
            _state.PosArm4 = _state.WorkHomeArm4;
            _state.PosAux = _state.WorkHomeAux;
        }
        else
        {
            string axis = args.ToUpper();
            switch (axis)
            {
                case "X": _state.PosX = _state.WorkHomeX; break;
                case "Z": _state.PosZ = _state.WorkHomeZ; break;
                case "THETA": _state.PosTheta = _state.WorkHomeTheta; break;
                case "ARM1": _state.PosArm1 = _state.WorkHomeArm1; break;
                case "ARM2": _state.PosArm2 = _state.WorkHomeArm2; break;
                case "ARM3": _state.PosArm3 = _state.WorkHomeArm3; break;
                case "ARM4": _state.PosArm4 = _state.WorkHomeArm4; break;
                case "AUX": _state.PosAux = _state.WorkHomeAux; break;
            }
        }

        _state.IsExecuting = false;
        return ResponseBuilder.BuildSuccess(echo);
    }

    /// <summary>
    /// @GXYYZZ; 取片动作系列
    /// </summary>
    private string HandleGet(string args, string cmdPrefix)
    {
        if (TryBeginMotion(cmdPrefix + args, SimulatedDelayMs, out string rejected))
            return rejected;

        if (ShouldSimulateFailure())
        {
            _state.IsExecuting = false;
            string errCode = GenerateRandomErrorCode();
            return ResponseBuilder.BuildFailure(cmdPrefix + args, errCode, $"{cmdPrefix} failed (simulated)");
        }

        // 解析参数
        var parsed = CommandParser.ParseMotionArgs(args);
        if (parsed == null)
        {
            _state.IsExecuting = false;
            return ResponseBuilder.BuildFailure(cmdPrefix + args, "99990003", "Invalid motion args");
        }

        var (finger, station, slot) = parsed!.Value;

        // 仿真：取片后手指上有 Wafer
        if (cmdPrefix == "G" || cmdPrefix == "GIN" || cmdPrefix == "GOT")
            SetArmWafer(finger, true);

        // 更新位置
        SetArmPos(finger, station * 10.0 + slot);

        _state.IsExecuting = false;
        return ResponseBuilder.BuildSuccess(cmdPrefix + args);
    }

    /// <summary>
    /// @PXYYZZ; 放片动作系列
    /// </summary>
    private string HandlePlace(string args, string cmdPrefix)
    {
        if (TryBeginMotion(cmdPrefix + args, SimulatedDelayMs, out string rejected))
            return rejected;

        if (ShouldSimulateFailure())
        {
            _state.IsExecuting = false;
            string errCode = GenerateRandomErrorCode();
            return ResponseBuilder.BuildFailure(cmdPrefix + args, errCode, $"{cmdPrefix} failed (simulated)");
        }

        var parsed = CommandParser.ParseMotionArgs(args);
        if (parsed == null)
        {
            _state.IsExecuting = false;
            return ResponseBuilder.BuildFailure(cmdPrefix + args, "99990003", "Invalid motion args");
        }

        var (finger, station, slot) = parsed!.Value;

        // 仿真：放片后手指上无 Wafer
        if (cmdPrefix == "P" || cmdPrefix == "PIN" || cmdPrefix == "POT")
            SetArmWafer(finger, false);

        // 更新位置
        SetArmPos(finger, station * 10.0 + slot);

        _state.IsExecuting = false;
        return ResponseBuilder.BuildSuccess(cmdPrefix + args);
    }

    /// <summary>
    /// @GAPXYYZZUVVWW; 取放一体动作（先取后放）。
    /// 协议要求两帧结果：先回 @G&lt;取片段&gt;，再回 @P&lt;放片段&gt;；
    /// 这里用换行拼接两帧，由 TcpServer 拆开依次按结果延迟发出。
    /// </summary>
    private string HandleGap(string args)
    {
        if (TryBeginMotion("GAP" + args, SimulatedDelayMs * 2, out string rejected)) // 取+放，耗时翻倍
            return rejected;

        if (ShouldSimulateFailure())
        {
            _state.IsExecuting = false;
            string errCode = GenerateRandomErrorCode();
            return ResponseBuilder.BuildFailure("GAP" + args, errCode, "GAP failed (simulated)");
        }

        var parsed = CommandParser.ParseGapArgs(args);
        if (parsed == null)
        {
            _state.IsExecuting = false;
            return ResponseBuilder.BuildFailure("GAP" + args, "99990003", "Invalid GAP args");
        }

        var (srcFinger, srcStation, srcSlot, dstFinger, dstStation, dstSlot) = parsed!.Value;

        // 仿真：取片手指拿到片，放片手指放下片
        SetArmWafer(srcFinger, true);
        SetArmWafer(dstFinger, false);
        SetArmPos(srcFinger, srcStation * 10.0 + srcSlot);
        SetArmPos(dstFinger, dstStation * 10.0 + dstSlot);

        // 两帧：@G + 取片段(前5位 XYYZZ)、@P + 放片段(后5位 UVVWW)
        string getFrame = ResponseBuilder.BuildSuccess("G" + args[..5]);
        string placeFrame = ResponseBuilder.BuildSuccess("P" + args[5..10]);

        _state.IsExecuting = false;
        return getFrame + "\n" + placeFrame;
    }

    // --- 辅助方法 ---

    /// <summary>
    /// 运动准入 + 可中止的运动耗时等待。
    /// 返回 true = 已被拒绝或被打断 (rejected 为要回的错误帧, 调用方直接 return);
    /// 返回 false = 等待正常结束, 继续执行运动结果。
    /// 占用检查: 一次一个运动 (贴近真机), 判定与置位在锁内原子完成 —— 每条命令各跑在自己的
    /// Task 上, check-then-set 会让两条运动双双通过; 仿真器要当"忙就该拒"这条性质的判官,
    /// 自己就得真拒得住。等待用 AbortSignal.Wait 替代 Sleep,
    /// 期间收到 SStop/PStop (信号被 Set) 即按"运动被打断"回错误帧。
    /// </summary>
    private bool TryBeginMotion(string echo, int durationMs, out string rejected)
    {
        lock (_motionLock)
        {
            if (_state.IsExecuting)
            {
                rejected = ResponseBuilder.BuildFailure(echo, "99990011", "Robot busy (motion in progress)");
                return true;
            }

            _state.AbortSignal.Reset();
            _state.IsExecuting = true;
        }

        if (_state.AbortSignal.Wait(durationMs))
        {
            _state.IsExecuting = false;
            rejected = ResponseBuilder.BuildFailure(echo, "99990030", "Motion aborted by SStop/PStop");
            return true;
        }

        rejected = string.Empty;
        return false;
    }

    /// <summary>设置指定手指(1-4)的片在位状态</summary>
    private void SetArmWafer(int finger, bool has)
    {
        if (_state.SuppressWaferUpdate)
        {
            return;   // 故障注入: 取放片后不更新手指在位, 让上位机的后置校验失败
        }
        switch (finger)
        {
            case 1: _state.Arm1HasWafer = has; break;
            case 2: _state.Arm2HasWafer = has; break;
            case 3: _state.Arm3HasWafer = has; break;
            case 4: _state.Arm4HasWafer = has; break;
        }
    }

    /// <summary>设置指定手指(1-4)的当前位置</summary>
    private void SetArmPos(int finger, double pos)
    {
        switch (finger)
        {
            case 1: _state.PosArm1 = pos; break;
            case 2: _state.PosArm2 = pos; break;
            case 3: _state.PosArm3 = pos; break;
            case 4: _state.PosArm4 = pos; break;
        }
    }

    private bool ShouldSimulateFailure()
    {
        return FailureRate > 0 && _random.Next(100) < FailureRate;
    }

    private string GenerateRandomErrorCode()
    {
        return _random.Next(10000000, 99999999).ToString();
    }
}
