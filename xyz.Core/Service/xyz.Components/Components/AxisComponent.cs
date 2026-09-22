using xyz.Components.Alarm;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Motion;

namespace xyz.Components.Components;

/// <summary>
/// 运动轴组件：一根走倍福数据块的伺服轴。指令同步写进命令块（SendPlcDataPath），状态每拍从状态块
/// （ReceivePlcDataPath）的缓存里取，两块都走 PLC 组件的整块缓存。
/// 动作由轴自己管到完成：调用方发 MoveTo 后看 ActionState，超时和驱动器报错由轴自报。
/// </summary>
[Component(description: "运动轴组件")]
public partial class AxisComponent : ComponentBase
{
    #region SC 装机常量

    [SCEditor("", "Plc", "下发给 PLC 的命令块名（指令/目标位置/速度写到这儿），空 = 接线未定", Required = true)]
    public string SendPlcDataPath { get; set; } = string.Empty;

    [SCEditor("", "Plc", "从 PLC 回读的状态块名（状态/实际位置/到位从这儿读），空 = 接线未定", Required = true)]
    public string ReceivePlcDataPath { get; set; } = string.Empty;

    #endregion

    #region EC 可调参数

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s", "0.1", "10000", "10", "回零速度")]
    public double HomeSpeed
    {
        get { return GetEcDouble(nameof(HomeSpeed)); }
        set { SetEcDouble(nameof(HomeSpeed), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s", "0.1", "10000", "50", "默认移动速度")]
    public double MoveSpeed
    {
        get { return GetEcDouble(nameof(MoveSpeed)); }
        set { SetEcDouble(nameof(MoveSpeed), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s2", "0.1", "100000", "1000", "加速度")]
    public double Accel
    {
        get { return GetEcDouble(nameof(Accel)); }
        set { SetEcDouble(nameof(Accel), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s2", "0.1", "100000", "1000", "减速度")]
    public double Decel
    {
        get { return GetEcDouble(nameof(Decel)); }
        set { SetEcDouble(nameof(Decel), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s", "0.1", "10000", "100", "最大速度")]
    public double MaxSpeed
    {
        get { return GetEcDouble(nameof(MaxSpeed)); }
        set { SetEcDouble(nameof(MaxSpeed), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "1000", "300000", "30000", "运动等待超时")]
    public int TimeoutMs
    {
        get { return GetEcInt(nameof(TimeoutMs)); }
        set { SetEcInt(nameof(TimeoutMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, "ms", "500", "60000", "5000", "停止等待超时")]
    public int StopTimeoutMs
    {
        get { return GetEcInt(nameof(StopTimeoutMs)); }
        set { SetEcInt(nameof(StopTimeoutMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit", "0.001", "100", "1.0", "到位容差")]
    public double PositionTolerance
    {
        get { return GetEcDouble(nameof(PositionTolerance)); }
        set { SetEcDouble(nameof(PositionTolerance), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Double, "unit/s", "0.1", "10000", "5", "速度容差")]
    public double SpeedTolerance
    {
        get { return GetEcDouble(nameof(SpeedTolerance)); }
        set { SetEcDouble(nameof(SpeedTolerance), value); }
    }

    #endregion

    #region SV 运行状态

    /// <summary>
    /// PLC 状态是否有效：状态块这一拍读到了、命令块基线也拿到了。false 时下面的数值是断线前的旧值，只能留着显示。
    /// </summary>
    [VariableMark(VariableType.SV, ValueFormat.Bool, description: "轴 PLC 状态是否有效")]
    public bool HasPlcData
    {
        get
        {
            lock (_axisGate)
            {
                return _hasPlcData;
            }
        }
    }

    /// <summary>状态整帧快照。</summary>
    public MotionPlcToCSharpData Status
    {
        get
        {
            lock (_axisGate)
            {
                return _status;
            }
        }
    }

    /// <summary>当前动作的状态：写进 PLC 即 Running，完成/失败由扫描判定；断线时在途动作直接 Failed。</summary>
    public ActionState ActionState
    {
        get
        {
            lock (_axisGate)
            {
                return _action;
            }
        }
    }

    [VariableMark(VariableType.SV, ValueFormat.Double, description: "轴实际位置（HasPlcData 为 false 时无效）")]
    public double CurrentPosition => Status.Current_Position;

    public double CurrentSpeed => Status.Current_Speed;

    public bool IsHomed => HasPlcData && Status.Is_Homed == 1;

    public bool IsInPosition => HasPlcData && Status.Is_In_Position == 1;

    public bool IsBusy => HasPlcData && Status.Is_Busy == 1;

    public bool IsServoOn => HasPlcData && Status.Is_Servo_On == 1;

    public bool IsError => HasPlcData && Status.Is_Err == 1;

    #endregion

    #region Alarm

    [Alarm("轴回零超时", AlarmCategory.Timeout,
        Description = "回零命令已发出，但在指定时间内未收到回零完成",
        Solution = "检查原点开关、驱动器使能及 PLC 数据块通讯；超时时长在 EC TimeoutMs 调")]
    public string HomeTimeoutAlarm = nameof(HomeTimeoutAlarm);

    [Alarm("轴运动超时", AlarmCategory.Timeout,
        Description = "运动命令已发出，但在指定时间内实际位置未进入目标位置容差",
        Solution = "检查驱动器使能、机械是否卡滞及 PLC 数据块通讯；超时时长在 EC TimeoutMs、容差在 EC PositionTolerance 调")]
    public string MoveTimeoutAlarm = nameof(MoveTimeoutAlarm);

    [Alarm("轴停止超时", AlarmCategory.Timeout,
        Description = "停止命令已发出，但在指定时间内轴未静止",
        Solution = "检查驱动器及 PLC 数据块通讯；超时时长在 EC StopTimeoutMs 调")]
    public string StopTimeoutAlarm = nameof(StopTimeoutAlarm);

    [Alarm("轴驱动器报错", AlarmCategory.MotionError,
        Description = "PLC 回读数据中该轴处于报错状态（驱动器报警、限位、伺服未使能等）",
        Solution = "查看驱动器报警码，排除故障后复位并重新回零")]
    public string AxisErrorAlarm = nameof(AxisErrorAlarm);

    #endregion
}
