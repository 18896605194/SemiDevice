using xyz.Components.Attributes;
using xyz.Drivers.Robot;

namespace xyz.Components.Components;

/// <summary>
/// 机械手驱动组件基座（薄壳）：配置与轴表、驱动生命周期、品牌无关的统一触发口。
/// 品牌差异全部关在品牌壳（如 RejeRobotComponent）里——同一个机型模块 sc.xml 换 Type 即换品牌，零代码。
/// 触发口下发即返回指令句柄：等 IsCompleted，结果读 Response。
/// </summary>
public abstract class RobotDriverComponent : ComponentBase
{
    #region SC

    [SCEditor("127.0.0.1", "Robot", "机械手网口 IP")]
    public string Host { get; set; } = "127.0.0.1";

    [SCEditor("9000", "Robot", "机械手网口端口")]
    public int NetPort { get; set; } = 9000;

    [SCEditor("X,Z,Theta,Arm1,Arm2", "Robot", "轴表（逗号分隔）：Arm* 个数 = 手指数，2 指机型 Arm1,Arm2，4 指再加 Arm3,Arm4")]
    public string Axes { get; set; } = "X,Z,Theta,Arm1,Arm2";

    #endregion

    #region 轴表

    private List<string>? _axisList;

    /// <summary>轴名列表（轴表配置解析）；查轴位置等按轴名触发。</summary>
    public IReadOnlyList<string> AxisList
    {
        get
        {
            if (_axisList is null)
            {
                _axisList = Axes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }

            return _axisList;
        }
    }

    /// <summary>手指数 = 轴表里 Arm* 的个数（手指号从 1 开始；晶圆账按手指注册槽位）。</summary>
    public int ArmCount
    {
        get { return AxisList.Count(axis => axis.StartsWith("Arm", StringComparison.OrdinalIgnoreCase)); }
    }

    #endregion

    #region 驱动连接

    /// <summary>品牌驱动（传输 + 帧编解码 + 指令收发）；Open 建好后有值。</summary>
    public IRobotDriver? Driver { get; private set; }

    public bool IsConnected
    {
        get { return Driver?.IsConnected ?? false; }
    }

    /// <summary>设备主动推送（在驱动路由线程上回调，事件已归一成 RobotDeviceEvent）。</summary>
    public event Action<RobotDeviceEvent>? DeviceEvent;

    /// <summary>
    /// 建驱动（首次）并打开连接。驱动建好先挂事件转发再开连：连上之后设备可能立刻推事件，晚订阅会漏。
    /// 可重复调用：已建好的驱动只重开连接。
    /// </summary>
    public bool Open()
    {
        var driver = Driver;
        if (driver is null)
        {
            driver = CreateDriver();
            driver.OnSpontaneousEvent += OnDeviceEvent;
            Driver = driver;
        }

        return driver.Open();
    }

    /// <summary>关闭连接；驱动保留，重开走 Open。</summary>
    public void Close()
    {
        Driver?.Close();
    }

    private void OnDeviceEvent(RobotDeviceEvent evt)
    {
        DeviceEvent?.Invoke(evt);
    }

    #endregion

    #region 统一触发口（品牌无关）

    /// <summary>Home：全轴回原点。</summary>
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

    /// <summary>QueryAxisPos：查指定轴当前坐标（轴名取 AxisList，结果在 Response.Position）。</summary>
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

    protected abstract RobotCommand CreateSubscribeWaferEvent();

    #endregion
}
