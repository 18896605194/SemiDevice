using xyz.Components.Motion;
using xyz.Common.Log;

namespace xyz.Components.Components;

public partial class AxisComponent
{
    #region 运行字段

    // 以下状态由 PLC 回调和模块扫描共享，访问时持有 _axisGate。
    private readonly object _axisGate = new();

    // 指令和反馈。
    private MotionCSharpToPlcCommand _command;
    private MotionPlcToCSharpData _status;

    // 当前动作。
    private long _operationStarted;
    private bool _observedMotion;
    private double _target;
    private AxisOperationState _operation;

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

    /// <summary>返回 true 只表示已接受待发指令；完成看 OperationState 和有效 PLC 状态。</summary>
    public bool Home()
    {
        return QueueCommand(MotionCommandId.Home, 0, HomeSpeed);
    }

    public bool MoveTo(double position, double? speed = null)
    {
        return QueueCommand(MotionCommandId.MoveTo, position, speed ?? MoveSpeed);
    }

    public bool MoveBy(double offset, double? speed = null)
    {
        return QueueCommand(MotionCommandId.MoveBy, offset, speed ?? MoveSpeed);
    }

    public bool Jog(double speed)
    {
        return QueueCommand(MotionCommandId.Jog, 0, speed);
    }

    public bool Spin(double speed)
    {
        return QueueCommand(MotionCommandId.Spin, 0, speed);
    }

    public bool Stop()
    {
        return QueueCommand(MotionCommandId.Stop, 0, 0, priority: true);
    }

    public bool EmergencyStop()
    {
        return QueueCommand(MotionCommandId.EStop, 0, 0, priority: true);
    }

    public bool ResetDrive()
    {
        return QueueCommand(MotionCommandId.Reset, 0, 0);
    }

    public bool SetServo(bool enabled)
    {
        return QueueCommand(MotionCommandId.Stop, 0, 0,
            priority: !enabled, servo: enabled ? (byte)1 : (byte)0);
    }

    #endregion

    #region 状态读取

    public bool TryGetStatus(out MotionPlcToCSharpData status)
    {
        lock (_axisGate)
        {
            status = _status;
            return HasValidPlcData();
        }
    }
    // 仅供持有 _axisGate 的内部代码使用。
    private bool HasValidPlcData()
    {
        // 输入初值和输出基线都读取成功后，才记录本次连接。
        return _plc is { IsConnected: true }
            && _subscribedGeneration == _plc.ConnectionGeneration;
    }

    #endregion

    #region PLC 订阅回调

    private void InitializeCommand(MotionCSharpToPlcCommand value, long generation, int version)
    {
        lock (_axisGate)
        {
            if (!IsCurrentSubscription(generation, version))
            {
                return;
            }

            _command = value;
            if (_operation is AxisOperationState.Pending or AxisOperationState.Running)
            {
                _operation = AxisOperationState.Failed;
            }
        }
    }

    private MotionCSharpToPlcCommand? GetDesiredCommand(long generation, int version)
    {
        lock (_axisGate)
        {
            if (!IsCurrentSubscription(generation, version))
            {
                return null;
            }

            if (!HasValidPlcData() || _operation != AxisOperationState.Pending)
            {
                return null;
            }

            return _command;
        }
    }

    private void OnCommandWritten(MotionCSharpToPlcCommand value, long generation, int version)
    {
        lock (_axisGate)
        {
            if (!IsCurrentSubscription(generation, version))
            {
                return;
            }

            if (!HasValidPlcData() || value.Command_Sync_No != _command.Command_Sync_No || _operation != AxisOperationState.Pending)
            {
                return;
            }

            _observedMotion = false;
            _operation = AxisOperationState.Running;
        }
    }

    private void OnStatusReceived(MotionPlcToCSharpData value, long generation, int version)
    {
        lock (_axisGate)
        {
            if (!IsCurrentSubscription(generation, version))
            {
                return;
            }

            _status = value;
            // Running 只在指令写成功后设置，此处自然是写入后的新反馈。
            if (_operation == AxisOperationState.Running)
            {
                _observedMotion |= value.Is_Busy == 1 || value.Is_Stopped == 0;
                CheckOperationCompletion();
            }
        }
    }

    #endregion

    #region 指令校验与组装

    private bool QueueCommand(
        MotionCommandId command,
        double position,
        double speed,
        bool priority = false,
        byte? servo = null)
    {
        lock (_axisGate)
        {
            // 先检查通信和指令占用，再检查本次动作的条件。
            if (!CanAcceptCommand(priority))
            {
                return false;
            }

            if (!double.IsFinite(position))
            {
                return false;
            }

            if (!double.IsFinite(speed))
            {
                return false;
            }

            double accel = Accel;
            double decel = Decel;
            double maxSpeed = MaxSpeed;
            if (!CanExecuteCommand(command, speed, accel, decel, maxSpeed))
            {
                return false;
            }

            // 相对位移换算成绝对目标，供后续到位判断使用。
            _target = position;
            if (command == MotionCommandId.MoveBy)
            {
                _target = _status.Current_Position + position;
            }

            if (!double.IsFinite(_target))
            {
                return false;
            }

            if (IsAlreadyAtTarget(command))
            {
                // 已在目标容差内无需再次下发，避免等待一个不会发生的 Busy 变化。
                _operation = AxisOperationState.Completed;
                return true;
            }

            _command = CreateCommand(command, position, speed, accel, decel, maxSpeed, servo);
            _operationStarted = Environment.TickCount64;
            _operation = AxisOperationState.Pending;
            return true;
        }
    }

    // 以下检查由 QueueCommand 在轴锁内调用。
    private bool CanAcceptCommand(bool priority)
    {
        if (!HasValidPlcData())
        {
            return false;
        }

        // 停止类指令允许替换待发动作。
        if (priority)
        {
            return true;
        }

        if (_operation == AxisOperationState.Pending)
        {
            return false;
        }

        if (_operation == AxisOperationState.Running)
        {
            return false;
        }

        return true;
    }

    private bool CanExecuteCommand(
        MotionCommandId command,
        double speed,
        double accel,
        double decel,
        double maxSpeed)
    {
        if (!double.IsFinite(decel) || decel <= 0)
        {
            return false;
        }

        switch (command)
        {
            case MotionCommandId.Home:
                if (speed <= 0)
                {
                    return false;
                }

                return CanStartMotion(speed, accel, maxSpeed);

            case MotionCommandId.MoveTo:
            case MotionCommandId.MoveBy:
                if (speed <= 0)
                {
                    return false;
                }

                if (_status.Is_Homed != 1)
                {
                    return false;
                }

                return CanStartMotion(speed, accel, maxSpeed);

            case MotionCommandId.Jog:
            case MotionCommandId.Spin:
                return CanStartMotion(speed, accel, maxSpeed);

            default:
                return true;
        }
    }

    private bool CanStartMotion(double speed, double accel, double maxSpeed)
    {
        // 设备条件。
        if (_status.Is_Err != 0)
        {
            return false;
        }

        if (_status.Is_Ready != 1)
        {
            return false;
        }

        if (_status.Is_Servo_On != 1)
        {
            return false;
        }

        if (_status.Is_Busy != 0)
        {
            return false;
        }

        // 运动参数。
        if (!double.IsFinite(accel) || accel <= 0)
        {
            return false;
        }

        if (!double.IsFinite(maxSpeed) || maxSpeed <= 0)
        {
            return false;
        }

        if (speed == 0 || Math.Abs(speed) > maxSpeed)
        {
            return false;
        }

        return true;
    }

    private bool IsAlreadyAtTarget(MotionCommandId command)
    {
        if (command != MotionCommandId.MoveTo && command != MotionCommandId.MoveBy)
        {
            return false;
        }

        if (_status.Is_Stopped != 1)
        {
            return false;
        }

        if (_status.Is_In_Position != 1)
        {
            return false;
        }

        return Math.Abs(_status.Current_Position - _target) <= PositionTolerance;
    }

    private MotionCSharpToPlcCommand CreateCommand(
        MotionCommandId command,
        double position,
        double speed,
        double accel,
        double decel,
        double maxSpeed,
        byte? servo)
    {
        return new MotionCSharpToPlcCommand
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
    }

    #endregion

    #region 动作完成判断

    private void CheckOperationCompletion()
    {
        if (!HasValidPlcData())
        {
            return;
        }

        var command = (MotionCommandId)_command.Axis_Command;
        if (_status.Is_Err == 1 && command is not MotionCommandId.Reset and not MotionCommandId.Stop and not MotionCommandId.EStop)
        {
            _operation = AxisOperationState.Failed;
            return;
        }

        bool completed = command switch
        {
            MotionCommandId.Home => _observedMotion && _status.Is_Homed == 1 && _status.Is_Busy == 0,
            MotionCommandId.MoveTo or MotionCommandId.MoveBy => _observedMotion && _status.Is_Busy == 0
                && _status.Is_In_Position == 1 && Math.Abs(_status.Current_Position - _target) <= PositionTolerance,
            MotionCommandId.Stop or MotionCommandId.EStop => _status.Is_Stopped == 1 && _status.Is_Busy == 0
                && _status.Is_Servo_On == _command.Axis_Servo,
            MotionCommandId.Reset => _status.Is_Err == 0,
            // 连续运动到速后允许后续命令，设备是否还在转由 IsBusy/CurrentSpeed 表示。
            MotionCommandId.Spin or MotionCommandId.Jog => _observedMotion
                && Math.Abs(_status.Current_Speed - _command.Param2) <= SpeedTolerance,
            _ => false,
        };
        if (completed)
        {
            _operation = AxisOperationState.Completed;
        }
    }

    #endregion

    #region 扫描与超时报警

    protected override void OnScan()
    {
        base.OnScan();
        EnsureSubscribed();
        string? alarm = null;
        lock (_axisGate)
        {
            if (!HasValidPlcData())
            {
                if (_operation is AxisOperationState.Pending or AxisOperationState.Running)
                {
                    _operation = AxisOperationState.Failed;
                }
                return;
            }

            if (_status.Is_Err == 1)
            {
                alarm = AxisErrorAlarm;
            }

            if (_operation is AxisOperationState.Pending or AxisOperationState.Running)
            {
                alarm = CheckOperationTimeout() ?? alarm;
            }
        }

        if (alarm is not null)
        {
            RaiseAlarm(alarm);
        }
    }

    private string? CheckOperationTimeout()
    {
        var command = (MotionCommandId)_command.Axis_Command;
        bool stopping = command is MotionCommandId.Stop or MotionCommandId.EStop;
        int timeout = stopping ? StopTimeoutMs : TimeoutMs;
        if (Environment.TickCount64 - _operationStarted < timeout)
        {
            return null;
        }

        _operation = AxisOperationState.Failed;

        return command switch
        {
            MotionCommandId.Home => HomeTimeoutAlarm,
            MotionCommandId.Stop or MotionCommandId.EStop => StopTimeoutAlarm,
            _ => MoveTimeoutAlarm,
        };
    }

    #endregion

}
