using xyz.Components.Attributes;
using xyz.Drivers.Communication;
using xyz.Drivers.Loadport;

namespace xyz.Components.Components;

/// <summary>
/// LoadPort 驱动组件基座：传输配置、驱动生命周期、主动事件转发、统一触发口。
/// 品牌只补两件事——建驱动、建指令；sc.xml 换品牌壳 Type 即换品牌，机型代码不动。
/// </summary>
public abstract class LoadPortDriverComponent : ComponentBase
{
    #region SC

    [SCEditor("Serial", "LoadPort", "通讯类型：Serial=串口，Tcp=网口")]
    public CommType CommType { get; set; } = CommType.Serial;

    [SCEditor("COM", "LoadPort", "串口名称（CommType=Serial 时生效）")]
    public string PortName { get; set; } = string.Empty;

    [SCEditor("9600", "LoadPort", "串口波特率（CommType=Serial 时生效）")]
    public int BaudRate { get; set; } = 9600;

    [SCEditor("None", "LoadPort", "串口校验位（CommType=Serial 时生效）")]
    public string Parity { get; set; } = "None";

    [SCEditor("8", "LoadPort", "串口数据位（CommType=Serial 时生效）")]
    public int DataBits { get; set; } = 8;

    [SCEditor("One", "LoadPort", "串口停止位（CommType=Serial 时生效）")]
    public string StopBits { get; set; } = "One";

    [SCEditor("192.168.1.100", "LoadPort", "网口 IP（CommType=Tcp 时生效）")]
    public string Host { get; set; } = "192.168.1.100";

    [SCEditor("4004", "LoadPort", "网口端口（CommType=Tcp 时生效）")]
    public int NetPort { get; set; } = 4004;

    #endregion

    #region 驱动连接

    public ILoadPortDriver? Driver { get; private set; }

    public bool IsConnected
    {
        get { return Driver?.IsConnected ?? false; }
    }

    /// <summary>设备主动推送（在驱动路由线程上回调，事件已归一成 LoadPortDeviceEvent）。</summary>
    public event Action<LoadPortDeviceEvent>? DeviceEvent;

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

    private void OnDeviceEvent(LoadPortDeviceEvent evt)
    {
        DeviceEvent?.Invoke(evt);
    }

    /// <summary>按 sc.xml 配的 CommType 建传输（串口/网口都吃配置，不像机械手只走网口）。</summary>
    protected ICommunication CreateTransport()
    {
        return CommunicationFactory.Create(CommType, PortName, BaudRate, Parity, DataBits, StopBits, Host, NetPort);
    }

    #endregion

    #region 统一触发口

    /// <summary>Load：开门 + Mapping（结果在 Response.SlotMap）。</summary>
    public LoadPortCommand? Load()
    {
        return Run(CreateLoad());
    }

    /// <summary>Unload：关门。</summary>
    public LoadPortCommand? Unload()
    {
        return Run(CreateUnload());
    }

    /// <summary>Home：整机回零。</summary>
    public LoadPortCommand? Home()
    {
        return Run(CreateHome());
    }

    /// <summary>Clamp：夹紧 FOUP。</summary>
    public LoadPortCommand? Clamp()
    {
        return Run(CreateClamp());
    }

    /// <summary>Unclamp：松开 FOUP。</summary>
    public LoadPortCommand? Unclamp()
    {
        return Run(CreateUnclamp());
    }

    /// <summary>Stop：急停（组件基类的 Abort 是中止上层操作，两回事）。</summary>
    public LoadPortCommand? Stop()
    {
        return Run(CreateStop());
    }

    /// <summary>ResetDrive：清设备报错（组件基类的 Reset 是清报警+复位子组件，两回事）。</summary>
    public LoadPortCommand? ResetDrive()
    {
        return Run(CreateResetDrive());
    }

    /// <summary>QueryStatus：查设备状态（结果在 Response.Status）。</summary>
    public LoadPortCommand? QueryStatus()
    {
        return Run(CreateQueryStatus());
    }

    /// <summary>QueryVersion：查设备版本。</summary>
    public LoadPortCommand? QueryVersion()
    {
        return Run(CreateQueryVersion());
    }

    /// <summary>受理下发一条指令：成功返回句柄（等 IsCompleted 读 Response），被拒（未连接或同键在途）返回 null。</summary>
    private static LoadPortCommand? Run(LoadPortCommand command)
    {
        if (command.Execute())
        {
            return command;
        }

        return null;
    }

    #endregion

    #region 品牌实现（造哪条指令、走什么传输，由品牌壳填）

    /// <summary>造品牌驱动（传输 + 品牌帧编解码 + 驱动）。</summary>
    protected abstract ILoadPortDriver CreateDriver();

    protected abstract LoadPortCommand CreateLoad();

    protected abstract LoadPortCommand CreateUnload();

    protected abstract LoadPortCommand CreateHome();

    protected abstract LoadPortCommand CreateClamp();

    protected abstract LoadPortCommand CreateUnclamp();

    protected abstract LoadPortCommand CreateStop();

    protected abstract LoadPortCommand CreateResetDrive();

    protected abstract LoadPortCommand CreateQueryStatus();

    protected abstract LoadPortCommand CreateQueryVersion();

    #endregion
}
