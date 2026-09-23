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

    /// <summary>
    /// 设备主动推送
    /// </summary>
    public event Action<LoadPortDeviceEvent>? DeviceEvent;


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

    public void Close()
    {
        Driver?.Close();
    }

    private void OnDeviceEvent(LoadPortDeviceEvent evt)
    {
        DeviceEvent?.Invoke(evt);
    }

    /// <summary>
    /// 返回通讯接口
    /// </summary>
    /// <returns></returns>
    protected ICommunication CreateTransport()
    {
        return CommunicationFactory.Create(CommType, PortName, BaudRate, Parity, DataBits, StopBits, Host, NetPort);
    }

    #endregion

    #region 统一触发口

    /// <summary>Load：开门 + Mapping（结果在 Response.SlotMap）。成功返回句柄（等 IsCompleted 读 Response），被拒返回 null。</summary>
    public LoadPortCommand? Load() => CreateLoad().Execute();

    /// <summary>Unload：关门。</summary>
    public LoadPortCommand? Unload() => CreateUnload().Execute();

    /// <summary>Home：整机回零。</summary>
    public LoadPortCommand? Home() => CreateHome().Execute();

    /// <summary>Clamp：夹紧 FOUP。</summary>
    public LoadPortCommand? Clamp() => CreateClamp().Execute();

    /// <summary>Unclamp：松开 FOUP。</summary>
    public LoadPortCommand? Unclamp() => CreateUnclamp().Execute();

    /// <summary>Stop：急停（组件基类的 Abort 是中止上层操作，两回事）。</summary>
    public LoadPortCommand? Stop() => CreateStop().Execute();

    /// <summary>ResetDrive：清设备报错（组件基类的 Reset 是清报警+复位子组件，两回事）。</summary>
    public LoadPortCommand? ResetDrive() => CreateResetDrive().Execute();

    /// <summary>QueryStatus：查设备状态（结果在 Response.Status）。</summary>
    public LoadPortCommand? QueryStatus() => CreateQueryStatus().Execute();

    /// <summary>QueryVersion：查设备版本。</summary>
    public LoadPortCommand? QueryVersion() => CreateQueryVersion().Execute();

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
