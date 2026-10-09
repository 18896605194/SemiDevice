using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Drivers.Communication;
using xyz.Drivers.Loadport;

namespace xyz.Components.Components;

/// <summary>
/// LoadPort 驱动组件基座：传输配置、驱动生命周期（断线重连）、主动事件转发、统一触发口、在途指令作废。
/// 品牌只补两件事——建驱动、建指令；sc.xml 换品牌壳 Type 即换品牌，机型代码不动。
/// 挂在 LoadPort 模块下面，OnScan 由父模块的扫描线程递归带着跑。
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

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "600000",
        @default: "5000", description: "通讯断开后隔多久重连一次")]
    public int ReconnectIntervalMs
    {
        get { return GetEcInt(nameof(ReconnectIntervalMs)); }
        set { SetEcInt(nameof(ReconnectIntervalMs), value); }
    }

    #endregion

    #region 驱动连接

    private readonly DriverReconnector _reconnector = new();

    public ILoadPortDriver? _driver { get; private set; }

    public bool IsConnected
    {
        get { return _driver?.IsConnected ?? false; }
    }

    /// <summary>
    /// 设备主动推送
    /// </summary>
    public event Action<LoadPortDeviceEvent>? DeviceEvent;

    /// <summary>
    /// 组件初始化（开机）：建驱动（首次）并打开连接。可重复调用：已建好的驱动只重开连接。
    /// 这一次连没连上，之后断了都由扫描线程按 EC ReconnectIntervalMs 在后台重连。
    /// </summary>
    public override bool InitComponent()
    {
        bool childrenInitialized = base.InitComponent();
        var driver = _driver;
        if (driver is null)
        {
            driver = CreateDriver();
            driver.OnSpontaneousEvent += OnDeviceEvent;
            _driver = driver;
        }

        _reconnector.Enable();
        bool opened = driver.Open();
        return opened && childrenInitialized;
    }

    /// <summary>
    /// 关连接（在途指令作废），不再重连。
    /// </summary>
    public void Close()
    {
        _reconnector.Disable();
        _driver?.Close();
    }

    /// <summary>
    /// 扫描（父模块的扫描线程带着跑）：断了按间隔在后台重连——先关（作废旧连接上的在途指令、收掉旧的收发任务）再开。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        var driver = _driver;
        if (driver is null)
        {
            return;
        }

        _reconnector.Check(FullPath, driver.IsConnected, ReconnectIntervalMs, () =>
        {
            driver.Close();
            driver.Open();
        });
    }

    /// <summary>
    /// 作废一条在途指令（回复丢了、等超时了）：让出它的在途位，同名指令能再发。
    /// </summary>
    public void Abandon(LoadPortCommand command, string reason)
    {
        _driver?.Abandon(command, reason);
    }

    /// <summary>
    /// 作废全部在途指令（动作没做成时用）。
    /// </summary>
    public void AbandonAll(string reason)
    {
        _driver?.AbandonAll(reason);
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
