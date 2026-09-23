using System.Runtime.InteropServices;
using xyz.Components.Interfaces;
using xyz.Components.Motion;

namespace xyz.Components.Components;

public partial class AxisComponent
{
    #region 运行字段
    private const int SettleScans = 3;

    private readonly object _axisGate = new();
    private IPlc? _plc;
    private MotionCSharpToPlcCommand _command;
    private bool _baselined;

    private MotionPlcToCSharpData _status;
    private bool _hasPlcData;

    // 当前动作。
    private ActionState _action;
    private long _actionStarted;
    private int _scansSinceSent;
    private bool _observedMotion;
    private double _target;

    #endregion

    #region 通信

    public bool Open(IPlc plc)
    {
        if (string.IsNullOrWhiteSpace(SendPlcDataPath) || string.IsNullOrWhiteSpace(ReceivePlcDataPath))
        {
            return false;
        }

        lock (_axisGate)
        {
            _plc = plc;
            _baselined = false;
            _hasPlcData = false;
        }

        plc.Register(SendPlcDataPath);  //注册轴
        plc.Register(ReceivePlcDataPath);
        return true;
    }

    #endregion

    #region 组件初始化、中止与复位

    public override object? Init()
    {
        base.Init();
        return Home();
    }

    public override object? Abort()
    {
        base.Abort();
        return Stop();
    }

    public override object? Reset()
    {
        if (!ResetDrive())
        {
            return false;
        }

        base.Reset();
        return true;
    }

    #endregion

    #region 轴操作

    public bool Home()
    {
        return Send(MotionCommandId.Home, 0, HomeSpeed);
    }

    public bool MoveTo(double position, double? speed = null)
    {
        return Send(MotionCommandId.MoveTo, position, speed ?? MoveSpeed);
    }

    public bool MoveBy(double offset, double? speed = null)
    {
        return Send(MotionCommandId.MoveBy, offset, speed ?? MoveSpeed);
    }

    public bool Jog(double speed)
    {
        return Send(MotionCommandId.Jog, 0, speed);
    }

    public bool Spin(double speed)
    {
        return Send(MotionCommandId.Spin, 0, speed);
    }

    public bool Stop()
    {
        return Send(MotionCommandId.Stop, 0, 0, priority: true);
    }

    public bool EmergencyStop()
    {
        return Send(MotionCommandId.EStop, 0, 0, priority: true);
    }

    public bool ResetDrive()
    {
        return Send(MotionCommandId.Reset, 0, 0);
    }

    /// <summary>
    /// 伺服使能是命令块里跟命令码并行的电平字节，改了随一条 Stop 下发让 PLC 锁存（轴静止时 Stop 没副作用）。
    /// 下使能可以打断在途动作，上使能要等上一条完成。
    /// </summary>
    public bool SetServo(bool enabled)
    {
        return Send(MotionCommandId.Stop, 0, 0, priority: !enabled, servo: enabled ? (byte)1 : (byte)0);
    }

    #endregion

    #region 发令

    /// <summary>
    /// 校验 → 组包 → 同步写进命令块。写成功即 Running，之后由扫描判完成/超时。
    /// 停止类（priority）可以打断在途动作，其他指令要等上一条完成。
    /// </summary>
    private bool Send(MotionCommandId command, double position, double speed, bool priority = false, byte? servo = null)
    {
        lock (_axisGate)
        {
            var plc = _plc;
            if (plc is null || !_hasPlcData)
            {
                return false;
            }

            if (!priority && _action == ActionState.Running)
            {
                return false;
            }

            if (!double.IsFinite(position) || !double.IsFinite(speed))
            {
                return false;
            }

            double accel = Accel;
            double decel = Decel;
            double maxSpeed = MaxSpeed;
            //参数校验
            if (!CanExecute(command, speed, accel, decel, maxSpeed))
            {
                return false;
            }

            // 相对位移换算成绝对目标，供到位判断用。
            _target = command == MotionCommandId.MoveBy ? _status.Current_Position + position : position;
            if (!double.IsFinite(_target))
            {
                return false;
            }

            //判断是否已经在目标状态和位置
            if (IsAlreadyAtTarget(command))
            {
                // 已在目标容差内不再下发，免得等一个不会发生的运动。
                _action = ActionState.Completed;
                return true;
            }

            var next = new MotionCSharpToPlcCommand
            {
                Axis_Command = (byte)command,
                Axis_Servo = servo ?? _command.Axis_Servo,
                Axis_Spare1 = _command.Axis_Spare1,
                Axis_Spare2 = _command.Axis_Spare2,
                Param1 = position,
                Param2 = speed,
                Param3 = accel,
                Param4 = decel,
                Param5 = maxSpeed,
                Param10 = 1,
                Command_Sync_No = unchecked(_command.Command_Sync_No + 1),
            };
            byte[] data = new byte[Marshal.SizeOf<MotionCSharpToPlcCommand>()];
            MemoryMarshal.Write(data, in next);
            if (!plc.WriteBlock(SendPlcDataPath, data))
            {
                return false;
            }

            _command = next;
            _action = ActionState.Running;
            _actionStarted = Environment.TickCount64;
            _scansSinceSent = 0;
            _observedMotion = false;
            return true;
        }
    }

    /// <summary>
    /// 发送指令动作参数检查
    /// </summary>
    /// <param name="command"></param>
    /// <param name="speed"></param>
    /// <param name="accel"></param>
    /// <param name="decel"></param>
    /// <param name="maxSpeed"></param>
    /// <returns></returns>
    private bool CanExecute(MotionCommandId command, double speed, double accel, double decel, double maxSpeed)
    {
        if (!double.IsFinite(decel) || decel <= 0)
        {
            return false;
        }

        switch (command)
        {
            case MotionCommandId.Home:
                return speed > 0 && CanStartMotion(speed, accel, maxSpeed);

            case MotionCommandId.MoveTo:
            case MotionCommandId.MoveBy:
                return speed > 0 && _status.Is_Homed == 1 && CanStartMotion(speed, accel, maxSpeed);

            case MotionCommandId.Jog:
            case MotionCommandId.Spin:
                return CanStartMotion(speed, accel, maxSpeed);

            default:
                return true;
        }
    }

    private bool CanStartMotion(double speed, double accel, double maxSpeed)
    {
        // 设备条件：没报错、就绪、使能、不忙。
        if (_status.Is_Err != 0 || _status.Is_Ready != 1 || _status.Is_Servo_On != 1 || _status.Is_Busy != 0)
        {
            return false;
        }

        // 运动参数。
        if (!double.IsFinite(accel) || accel <= 0 || !double.IsFinite(maxSpeed) || maxSpeed <= 0)
        {
            return false;
        }

        return speed != 0 && Math.Abs(speed) <= maxSpeed;
    }

    private bool IsAlreadyAtTarget(MotionCommandId command)
    {
        if (command != MotionCommandId.MoveTo && command != MotionCommandId.MoveBy)
        {
            return false;
        }

        return _status.Is_Stopped == 1
            && _status.Is_In_Position == 1
            && Math.Abs(_status.Current_Position - _target) <= PositionTolerance;
    }

    #endregion

    #region 扫描：读状态、判完成、判超时

    protected override void OnScan()
    {
        base.OnScan();

        string? alarm = null;
        lock (_axisGate)
        {
            if (!ReadPlc())
            {
                // 读不到就是断了：在途动作作废，重连后重新取基线。
                _hasPlcData = false;
                _baselined = false;
                if (_action == ActionState.Running)
                {
                    _action = ActionState.Failed;
                }

                return;
            }

            if (_status.Is_Err == 1)
            {
                alarm = AxisErrorAlarm;
            }

            if (_action == ActionState.Running)
            {
                _scansSinceSent++;
                _observedMotion |= _status.Is_Busy == 1 || _status.Is_Stopped == 0;
                CheckCompletion();
                if (_action == ActionState.Running)
                {
                    alarm = CheckTimeout() ?? alarm;
                }
            }
        }

        if (alarm is not null)
        {
            RaiseAlarm(alarm);
        }
    }

    /// <summary>
    /// 从 PLC 缓存取状态块；连上后的第一拍还要把命令块读回来当同步号基线。两块都到手才算有数据。
    /// </summary>
    private bool ReadPlc()
    {
        var plc = _plc;
        if (plc is null || !TryRead(plc, ReceivePlcDataPath, out MotionPlcToCSharpData status))
        {
            return false;
        }

        if (!_baselined)
        {
            if (!TryRead(plc, SendPlcDataPath, out MotionCSharpToPlcCommand baseline))
            {
                return false;
            }

            _command = baseline;
            _baselined = true;
        }

        _status = status;
        _hasPlcData = true;
        return true;
    }

    private static bool TryRead<T>(IPlc plc, string path, out T value) where T : unmanaged
    {
        value = default;
        if (!plc.TryReadBlock(path, out var block) || block.Length < Marshal.SizeOf<T>())
        {
            return false;
        }

        value = MemoryMarshal.Read<T>(block);
        return true;
    }

    private void CheckCompletion()
    {
        var command = (MotionCommandId)_command.Axis_Command;
        if (_status.Is_Err == 1
            && command != MotionCommandId.Reset
            && command != MotionCommandId.Stop
            && command != MotionCommandId.EStop)
        {
            _action = ActionState.Failed;
            return;
        }

        // 看到过"运动中"或者已过了旧帧窗口，这一帧的停稳/到位才作数。
        bool settled = _observedMotion || _scansSinceSent >= SettleScans;
        if (!settled)
        {
            return;
        }

        bool completed;
        switch (command)
        {
            case MotionCommandId.Home:
                completed = _status.Is_Homed == 1 && _status.Is_Busy == 0 && _status.Is_Stopped == 1;
                break;

            case MotionCommandId.MoveTo:
            case MotionCommandId.MoveBy:
                completed = _status.Is_Busy == 0&& _status.Is_In_Position == 1&& Math.Abs(_status.Current_Position - _target) <= PositionTolerance;
                break;

            case MotionCommandId.Stop:
            case MotionCommandId.EStop:
                completed = _status.Is_Stopped == 1 && _status.Is_Busy == 0&& _status.Is_Servo_On == _command.Axis_Servo;
                break;

            case MotionCommandId.Reset:
                completed = _status.Is_Err == 0;
                break;

            // 连续运动到速即完成，设备是否还在转看 IsBusy/CurrentSpeed。
            case MotionCommandId.Spin:
            case MotionCommandId.Jog:
                completed = Math.Abs(_status.Current_Speed - _command.Param2) <= SpeedTolerance;
                break;

            default:
                completed = false;
                break;
        }

        if (completed)
        {
            _action = ActionState.Completed;
        }
    }

    private string? CheckTimeout()
    {
        var command = (MotionCommandId)_command.Axis_Command;
        bool stopping = command == MotionCommandId.Stop || command == MotionCommandId.EStop;
        if (Environment.TickCount64 - _actionStarted < (stopping ? StopTimeoutMs : TimeoutMs))
        {
            return null;
        }

        _action = ActionState.Failed;
        switch (command)
        {
            case MotionCommandId.Home:
                return HomeTimeoutAlarm;

            case MotionCommandId.Stop:
            case MotionCommandId.EStop:
                return StopTimeoutAlarm;

            default:
                return MoveTimeoutAlarm;
        }
    }

    #endregion
}
