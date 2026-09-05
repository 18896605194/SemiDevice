namespace RejeRobotSimulator.State;

/// <summary>
/// 机器人仿真状态，所有指令共享的运行时数据
/// </summary>
public class RobotState
{
    // --- 基本状态 ---
    public bool HasError { get; set; } = false;

    /// <summary>故障注入: true 时 Pick/Place 不更新手指在位状态 (让上位机取放片后置校验失败)。</summary>
    public bool SuppressWaferUpdate { get; set; } = false;
    public string ErrorCode { get; set; } = "00000000";
    public string ErrorDesc { get; set; } = "";

    public string ProjectName { get; set; } = "DefaultProject";
    public string ProgramName { get; set; } = "DefaultProgram";

    // --- 压力表（4 臂）---
    public bool Arm1Pressure { get; set; } = true;
    public bool Arm2Pressure { get; set; } = true;
    public bool Arm3Pressure { get; set; } = true;
    public bool Arm4Pressure { get; set; } = true;

    // --- 运行状态 ---
    public bool IsExecuting { get; set; } = false;

    public bool IsEnabled { get; set; } = true;           // 伺服使能
    public int OpMode { get; set; } = 2;                   // 1=示教 2=自动 3=远程

    /// <summary>
    /// 运动中止信号: SStop/PStop 处理器 Set, 运动指令的可中止等待被唤醒后按"运动被打断"回错误帧。
    /// 每次运动开始前 Reset。
    /// </summary>
    public ManualResetEventSlim AbortSignal { get; } = new(false);

    // --- 速度 ---
    public int SpeedPercent { get; set; } = 50;

    // --- 各轴位置 (mm / degree) ---
    public double PosX { get; set; } = 0;
    public double PosZ { get; set; } = 0;
    public double PosTheta { get; set; } = 0;
    public double PosArm1 { get; set; } = 0;
    public double PosArm2 { get; set; } = 0;
    public double PosArm3 { get; set; } = 0;
    public double PosArm4 { get; set; } = 0;
    public double PosAux { get; set; } = 0;
    public double PosFlip1 { get; set; } = 0;
    public double PosFlip2 { get; set; } = 0;

    // --- Arm 间距 ---
    public double ArmDistance { get; set; } = 13.05;

    // --- 真空（4 臂电磁阀）---
    public bool EMV1 { get; set; } = false;
    public bool EMV2 { get; set; } = false;
    public bool EMV3 { get; set; } = false;
    public bool EMV4 { get; set; } = false;

    // --- 心跳 ---
    public bool HeartbeatEnabled { get; set; } = false;
    public int HeartbeatIntervalMs { get; set; } = 3000;

    // --- 轴参数 ---
    public double WorkHomeX { get; set; } = 0;
    public double WorkHomeZ { get; set; } = 0;
    public double WorkHomeTheta { get; set; } = 0;
    public double WorkHomeArm1 { get; set; } = 0;
    public double WorkHomeArm2 { get; set; } = 0;
    public double WorkHomeArm3 { get; set; } = 0;
    public double WorkHomeArm4 { get; set; } = 0;
    public double WorkHomeAux { get; set; } = 0;

    public double PosLimitX { get; set; } = 500;
    public double NegLimitX { get; set; } = -500;
    public double PosLimitZ { get; set; } = 700;
    public double NegLimitZ { get; set; } = 0;
    public double PosLimitTheta { get; set; } = 180;
    public double NegLimitTheta { get; set; } = -180;
    public double PosLimitArm1 { get; set; } = 400;
    public double NegLimitArm1 { get; set; } = 0;
    public double PosLimitArm2 { get; set; } = 400;
    public double NegLimitArm2 { get; set; } = 0;
    public double PosLimitArm3 { get; set; } = 400;
    public double NegLimitArm3 { get; set; } = 0;
    public double PosLimitArm4 { get; set; } = 400;
    public double NegLimitArm4 { get; set; } = 0;
    public double PosLimitAux { get; set; } = 200;
    public double NegLimitAux { get; set; } = -200;

    public double MaxVelX { get; set; } = 500;
    public double MaxVelZ { get; set; } = 500;
    public double MaxVelTheta { get; set; } = 360;
    public double MaxVelArm1 { get; set; } = 500;
    public double MaxVelArm2 { get; set; } = 500;
    public double MaxVelArm3 { get; set; } = 500;
    public double MaxVelArm4 { get; set; } = 500;
    public double MaxVelAux { get; set; } = 200;

    // --- AWC ---
    public int AwcStation { get; set; } = 0;
    public int AwcSlot { get; set; } = 0;
    public double AwcOffsetX { get; set; } = 0;
    public double AwcOffsetY { get; set; } = 0;
    public double AwcMaxX { get; set; } = 10;
    public double AwcMaxY { get; set; } = 10;

    // --- 功能开关 ---
    public bool DeadManErrorEnabled { get; set; } = true;   // DMO/DMC
    public bool ActiveErrorEnabled { get; set; } = true;    // AEO/AEC
    public bool SlideDetectEnabled { get; set; } = false;   // SWO/SWC

    // --- Z轴速度比率 ---
    public int ZLoadSpeedRatio { get; set; } = 100;         // ZLS 放片
    public int ZFetchSpeedRatio { get; set; } = 100;        // ZFS 取片

    // --- Arm伸出时其他轴可移动范围 ---
    public double AxisRangeZ { get; set; } = 20;
    public double AxisRangeR { get; set; } = 20;

    // --- Wafer 订阅 ---
    public bool WaferSubscribed { get; set; } = false;

    // --- Wafer 在位 (仿真，4 臂) ---
    /// <summary>
    /// 手指在位变化时触发 (armNo 1-4, hasWafer)。真机是控制器检测到即推 SubWaferEx,
    /// 故这里也事件即推, 不靠 UI 定时器比对快照 (UI 定时器是最低优先级, 一忙就被饿住,
    /// 上位机取放片后置校验会误判失败)。
    /// 事件在 TCP 后台线程触发, 订阅方 Broadcast 自带写锁, 跨线程安全。
    /// </summary>
    public event Action<int, bool>? ArmWaferChanged;

    private bool _arm1HasWafer;
    private bool _arm2HasWafer;
    private bool _arm3HasWafer;
    private bool _arm4HasWafer;

    public bool Arm1HasWafer
    {
        get { return _arm1HasWafer; }
        set { UpdateArmWafer(ref _arm1HasWafer, 1, value); }
    }

    public bool Arm2HasWafer
    {
        get { return _arm2HasWafer; }
        set { UpdateArmWafer(ref _arm2HasWafer, 2, value); }
    }

    public bool Arm3HasWafer
    {
        get { return _arm3HasWafer; }
        set { UpdateArmWafer(ref _arm3HasWafer, 3, value); }
    }

    public bool Arm4HasWafer
    {
        get { return _arm4HasWafer; }
        set { UpdateArmWafer(ref _arm4HasWafer, 4, value); }
    }

    private void UpdateArmWafer(ref bool field, int armNo, bool value)
    {
        if (field == value)
        {
            return;
        }
        field = value;
        ArmWaferChanged?.Invoke(armNo, value);
    }

    // --- 驱动器错误 (仿真) ---
    public string DriveErrorCode { get; set; } = "00000000";
    public string DriveErrorDesc { get; set; } = "No error";

    /// <summary>
    /// 重置为初始状态
    /// </summary>
    public void Reset()
    {
        HasError = false;
        ErrorCode = "00000000";
        ErrorDesc = "";
        IsExecuting = false;
    }
}
