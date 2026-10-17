using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Drivers.Robot;

namespace xyz.Components.Components;

/// <summary>
/// 机械手驱动组件基座：通讯配置、驱动生命周期（断线重连）、主动事件转发、统一触发口、在途指令作废。
/// 挂在机械手模块下面，OnScan 由父模块的扫描线程递归带着跑。
/// </summary>
public abstract class RobotDriverComponent : ComponentBase, IRobotDriverComponent
{
    #region SC

    [SCEditor("127.0.0.1", "Robot", "机械手网口 IP")]
    public string Host { get; set; } = "127.0.0.1";

    [SCEditor("9000", "Robot", "机械手网口端口")]
    public int NetPort { get; set; } = 9000;

    // 轴表不在这儿配：模块下 X/Z/Theta 用 RobotAxisComponent、手指用 RobotArmComponent 节点，节点名即轴名。

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "5000", description: "通讯断开后隔多久重连一次")]
    public int ReconnectIntervalMs
    {
        get { return GetEcInt(nameof(ReconnectIntervalMs)); }
        set { SetEcInt(nameof(ReconnectIntervalMs), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, min: "0", max: "1000",
        @default: "10", description: "通讯断开后最多重连几次，还连不上就停下、报警，人工复位后重新开始连；0 = 不自动重连")]
    public int ReconnectMaxCount
    {
        get { return GetEcInt(nameof(ReconnectMaxCount)); }
        set { SetEcInt(nameof(ReconnectMaxCount), value); }
    }

    #endregion

    #region Alarm

    [Alarm("机械手通讯断开，已停止重连", AlarmCategory.CommunicationError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "机械手控制器通讯断开后按 EC ReconnectMaxCount 重连了几次都没连上，已停止重连，指令都发不出去",
        Solution = "检查网线、控制器电源和通讯配置（sc.xml 的 IP / 端口）；好了以后复位，复位会重新开始连")]
    public string ReconnectFailedAlarm = nameof(ReconnectFailedAlarm);

    #endregion

    #region 驱动连接

    private readonly DriverReconnector _reconnector = new();

    public IRobotDriver? Driver { get; private set; }

    public bool IsConnected
    {
        get { return Driver?.IsConnected ?? false; }
    }

    /// <summary>设备主动推送（在驱动路由线程上回调，事件已归一成 RobotDeviceEvent）。</summary>
    public event Action<RobotDeviceEvent>? DeviceEvent;

    /// <summary>
    /// 组件初始化（开机）：建驱动（首次）并打开连接。驱动建好先挂事件转发再开连：连上之后设备可能立刻推事件，晚订阅会漏。
    /// 可重复调用：已建好的驱动只重开连接。这一次连没连上，之后断了都由扫描线程按 EC ReconnectIntervalMs、ReconnectMaxCount 在后台重连。
    /// </summary>
    public override bool InitComponent()
    {
        bool childrenInitialized = base.InitComponent();
        var driver = Driver;
        if (driver is null)
        {
            driver = CreateDriver();
            driver.OnSpontaneousEvent += OnDeviceEvent;
            Driver = driver;
        }

        _reconnector.Enable();
        bool opened = driver.Open();
        return opened && childrenInitialized;
    }

    /// <summary>关闭连接（在途指令作废），不再重连；驱动保留，重开走 InitComponent。</summary>
    public void Close()
    {
        _reconnector.Disable();
        Driver?.Close();
    }

    /// <summary>
    /// 扫描（父模块的扫描线程带着跑）：断了按间隔在后台重连——先关（作废旧连接上的在途指令、收掉旧的收发任务）再开。
    /// 机械手控制器断电重启、比后端晚上电时，不用再重启后端。重连满 EC ReconnectMaxCount 次还连不上就停下、报警。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        var driver = Driver;
        if (driver is null)
        {
            return;
        }

        bool gaveUp = _reconnector.Check(FullPath, driver.IsConnected, ReconnectIntervalMs, ReconnectMaxCount, () =>
        {
            driver.Close();
            driver.Open();
        });
        if (gaveUp)
        {
            RaiseAlarm(ReconnectFailedAlarm);
        }
    }

    /// <summary>
    /// 人工复位：重连次数清零、马上重新开始连（停下来的也接着连），再清本组件的报警。
    /// </summary>
    public override object? Reset()
    {
        _reconnector.Rearm();
        return base.Reset();
    }

    /// <summary>
    /// 作废一条在途指令（回复丢了、等超时了）：让出它的在途位，同名指令能再发。
    /// </summary>
    public void Abandon(RobotCommand command, string reason)
    {
        Driver?.Abandon(command, reason);
    }

    private void OnDeviceEvent(RobotDeviceEvent evt)
    {
        DeviceEvent?.Invoke(evt);
    }

    #endregion

    #region 统一触发口

    public RobotCommand? Home()
    {
        return Run(CreateHome());
    }

    /// <summary>Pick：arm 指定手指从 station 设备站点号的 slot 槽位取片。</summary>
    public RobotCommand? Pick(int arm, int station, int slot)
    {
        return Run(CreatePick(arm, station, slot));
    }

    /// <summary>Place：arm 指定手指向 station 设备站点号的 slot 槽位放片。</summary>
    public RobotCommand? Place(int arm, int station, int slot)
    {
        return Run(CreatePlace(arm, station, slot));
    }

    /// <summary>PowerOn：伺服上使能。</summary>
    public RobotCommand? PowerOn()
    {
        return Run(CreatePowerOn());
    }

    /// <summary>PowerOff：伺服下使能。</summary>
    public RobotCommand? PowerOff()
    {
        return Run(CreatePowerOff());
    }

    /// <summary>Stop：急停，可打断在途运动指令（被打断的由驱动以失败终结）。</summary>
    public RobotCommand? Stop()
    {
        return Run(CreateStop());
    }

    /// <summary>ResetDrive：清设备报错（组件基类的 Reset 是清报警+复位子组件，两回事）。</summary>
    public RobotCommand? ResetDrive()
    {
        return Run(CreateResetDrive());
    }

    /// <summary>QueryAxisPos：查指定轴当前坐标（轴名取 sc.xml 轴节点名，结果在 Response.Position）。</summary>
    public RobotCommand? QueryAxisPos(string axis)
    {
        return Run(CreateQueryAxisPos(axis));
    }

    /// <summary>QueryDeviceError：查设备当前报错（结果在 Response.DeviceError）。</summary>
    public RobotCommand? QueryDeviceError()
    {
        return Run(CreateQueryDeviceError());
    }

    /// <summary>QueryServoOn：查伺服使能状态（结果在 Response.ServoOn）。</summary>
    public RobotCommand? QueryServoOn()
    {
        return Run(CreateQueryServoOn());
    }

    /// <summary>QuerySpeed：查全局速度百分比（结果在 Response.Speed）。</summary>
    public RobotCommand? QuerySpeed()
    {
        return Run(CreateQuerySpeed());
    }

    /// <summary>SubscribeWaferEvent：订阅手指在位主动推送。</summary>
    public RobotCommand? SubscribeWaferEvent()
    {
        return Run(CreateSubscribeWaferEvent());
    }

    /// <summary>受理下发一条指令：成功返回句柄（等 IsCompleted 读 Response），被拒（未连接或同键在途）返回 null。</summary>
    private static RobotCommand? Run(RobotCommand command)
    {
        if (command.Execute())
        {
            return command;
        }

        return null;
    }

    #endregion

    #region 品牌实现（造哪条指令、走什么传输，由品牌壳填）

    /// <summary>造品牌驱动（传输 + 品牌帧编解码 + 驱动）；传输走串口还是网口由品牌自己定。</summary>
    protected abstract IRobotDriver CreateDriver();

    protected abstract RobotCommand CreateHome();

    protected abstract RobotCommand CreatePick(int arm, int station, int slot);

    protected abstract RobotCommand CreatePlace(int arm, int station, int slot);

    protected abstract RobotCommand CreatePowerOn();

    protected abstract RobotCommand CreatePowerOff();

    protected abstract RobotCommand CreateStop();

    protected abstract RobotCommand CreateResetDrive();

    protected abstract RobotCommand CreateQueryAxisPos(string axis);

    protected abstract RobotCommand CreateQueryDeviceError();

    protected abstract RobotCommand CreateQueryServoOn();

    protected abstract RobotCommand CreateQuerySpeed();

    protected abstract RobotCommand CreateSubscribeWaferEvent();

    #endregion
}
