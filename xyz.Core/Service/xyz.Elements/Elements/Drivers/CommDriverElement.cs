using xyz.Elements.Attributes;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Drivers.Communication;
using xyz.Drivers.Device;

namespace xyz.Elements.Elements;

/// <summary>
/// 设备通道的公共部分（machine.xml 里设备下面的通道节点）：管连接和断线重连，只把指令发下去、把回复交给指令；用哪家的解码器由所属设备开机时交给它。
/// 串口、网口各是一个子类（SerialDriverElement / TcpDriverElement），只管怎么建字节传输
/// </summary>
public abstract class CommDriverElement : ElementBase, ICommDriver
{
    #region Machine

    [Machine("True", "Driver", "启用这条通道：一个设备下面串口、网口都配上时只能启用一条（换接法就对调两边的开关）；没启用的不连、不重连")]
    public bool IsEnable { get; set; } = true;

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

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "100", max: "600000",
        @default: "3000", description: "设备状态查询超时")]
    public int QueryDataTimeOut
    {
        get { return GetEcInt(nameof(QueryDataTimeOut)); }
        set { SetEcInt(nameof(QueryDataTimeOut), value); }
    }

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "100", max: "600000",
        @default: "1000", description: "设备状态查询周期")]
    public int QueryStatusIntervalMs
    {
        get { return GetEcInt(nameof(QueryStatusIntervalMs)); }
        set { SetEcInt(nameof(QueryStatusIntervalMs), value); }
    }

    #endregion

    #region Alarm

    [Alarm("设备通讯断开，已停止重连", AlarmCategory.IrrecoverableError,
        AlarmLevel = AlarmLevel.Light,
        Description = "通讯断开后按 EC ReconnectMaxCount 重连了几次都没连上，已停止重连，这一路指令都发不出去",
        Solution = "检查线缆、设备电源和通讯配置（machine.xml 里这个通道节点的串口 / IP / 端口）；好了以后复位，复位会重新开始连")]
    public string ReconnectFailedAlarm = nameof(ReconnectFailedAlarm);

    #endregion

    #region 连接

    private readonly DriverReconnector _reconnector = new();
    private IFrameCodec? _codec;
    private DeviceDriver? _driver;

    /// <summary>
    /// 挂它的那个元件（模块或读头），开机 Attach 时交过来
    /// </summary>
    private ICommOwner? _owner;

    /// <summary>
    /// 挂它的元件要轮询的话就是它，不轮询的（读头）是 null
    /// </summary>
    private ICommModule? _module;

    /// <summary>
    /// 状态轮询的节奏，头一次轮询时建
    /// </summary>
    private StatusQueryLoop? _queries;

    public bool IsConnected
    {
        get { return _driver?.IsConnected ?? false; }
    }

    /// <summary>
    /// 设备主动上报的报文，在解析线程上回调
    /// </summary>
    public event Action<object>? Reported;

    /// <summary>
    /// 找设备直接挂着的、启用了的那条通道（machine.xml 里设备下面的通道节点）：一条都没启用为 null，启用了两条开机就报
    /// </summary>
    public static ICommDriver? FindIn(ElementBase owner)
    {
        ICommDriver? found = null;
        foreach (var child in owner.Children)
        {
            if (child is ICommDriver driver && driver.IsEnable)
            {
                if (found is not null)
                {
                    throw new InvalidOperationException(
                        $"machine.xml 节点 {owner.FullPath} 下面启用了两条设备通道，一个设备只能启用一条（串口、网口都配上时用 IsEnable 选一条）。");
                }

                found = driver;
            }
        }

        return found;
    }

    /// <summary>
    /// 挂它的元件开机时调（在打开之前，可重入）：拿它的解码器；它要轮询的话，通道以后照它查、把主动上报转给它
    /// </summary>
    public void Attach(ICommOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_owner is not null && !ReferenceEquals(_owner, owner))
        {
            throw new InvalidOperationException($"{FullPath} 已经交给 {_owner.GetType().Name} 用了，一条通道只能挂一个主。");
        }

        // 解码器只要一次：开机可重入，重入时不另建，免得跟已经打开的连接对不上
        if (_codec is null)
        {
            var codec = owner.CreateCodec();
            ArgumentNullException.ThrowIfNull(codec);
            _codec = codec;
        }

        _owner = owner;

        // 要轮询、要收主动上报的才挂；读头这种只要解码器的不挂。先摘后挂：开机可重入，只挂一份
        var module = owner as ICommModule;
        if (module is null)
        {
            return;
        }

        Reported -= module.OnReport;
        Reported += module.OnReport;
        _module = module;
    }

    /// <summary>
    /// 元件初始化（开机）：启用的建通道（头一次）并打开连接，这一次连没连上，之后断了都由扫描线程按 EC 在后台重连；没启用的什么都不做
    /// </summary>
    public override bool InitElement()
    {
        bool childrenInitialized = base.InitElement();
        if (!IsEnable)
        {
            return childrenInitialized;
        }

        var driver = _driver;
        if (driver is null)
        {
            var codec = _codec;
            if (codec is null)
            {
                throw new InvalidOperationException($"{FullPath} 没有解码器：挂它的设备要在开机时把自己交给它（Attach）。");
            }

            driver = new DeviceDriver(CreateCommunication(codec), codec as IFrameLink, this);
            driver.Reported += OnDriverReported;
            _driver = driver;
        }

        _reconnector.Enable();
        bool opened = driver.Open();
        return opened && childrenInitialized;
    }

    /// <summary>
    /// 关连接（在途指令作废），不再重连；重开走 InitElement
    /// </summary>
    public void Close()
    {
        _reconnector.Disable();
        _driver?.Close();
    }

    /// <summary>
    /// 扫描（所属设备的扫描线程带着跑，排在所属设备自己这一拍之前）：先断线重连，再替挂它的模块轮询一拍状态。
    /// 没启用的没建通道，什么都不做
    /// </summary>
    protected override void OnScan()
    {
        if (_driver is null)
        {
            return;
        }

        // 断了按间隔在后台重连——先关（作废旧连接上的在途指令）再开；重连满 EC 次数还连不上就停下、报警
        bool gaveUp = _reconnector.Check(this, _driver.IsConnected, ReconnectIntervalMs, ReconnectMaxCount, () =>
        {
            _driver.Close();
            _driver.Open();
        });
        if (gaveUp)
        {
            RaiseAlarm(ReconnectFailedAlarm);
        }

        LoopQueryStatus();
    }

    /// <summary>
    /// 状态轮询这一拍：一圈查什么、查到写哪由挂它的模块给，这里只管节奏和出事时通知它清状态
    /// </summary>
    private void LoopQueryStatus()
    {
        var module = _module;
        if (module is null)
        {
            return;
        }

        _queries ??= new StatusQueryLoop(this);
        var happened = _queries.Scan(this, module.QueryDataLoop, QueryStatusIntervalMs, QueryDataTimeOut);
        if (happened == StatusQueryEvent.TimedOut)
        {
            module.ClearQueriedStatus();
            return;
        }

        if (happened == StatusQueryEvent.Disconnected)
        {
            module.ClearQueriedStatus();
            module.OnDisconnected();
        }
    }

    /// <summary>
    /// 人工复位：重连次数清零、马上重新开始连（停下来的也接着连），再清本元件的报警
    /// </summary>
    public override object? Reset()
    {
        _reconnector.Rearm();
        return base.Reset();
    }

    /// <summary>
    /// 帧通讯：字节传输套上解码器（冒烟换成假通道就重写它）
    /// </summary>
    protected virtual IFrameCommunication CreateCommunication(IFrameCodec codec)
    {
        return new FrameCommunication(CreateTransport(), codec);
    }

    /// <summary>
    /// 字节传输：串口通道建串口、网口通道建网口
    /// </summary>
    protected abstract ICommunication CreateTransport();

    private void OnDriverReported(object message)
    {
        Reported?.Invoke(message);
    }

    #endregion

    #region 收发

    /// <summary>
    /// 发一条指令；没启用、没连上、同键指令还在途就被拒
    /// </summary>
    public bool Send(DeviceCommand command)
    {
        if (_driver is null)
        {
            return false;
        }

        return _driver.Send(command);
    }

    /// <summary>
    /// 作废一条在途指令：让出它的在途位，同键指令能再发
    /// </summary>
    public void Abandon(DeviceCommand command, string reason)
    {
        _driver?.Abandon(command, reason);
    }

    /// <summary>
    /// 作废全部在途指令
    /// </summary>
    public void AbandonAll(string reason)
    {
        _driver?.AbandonAll(reason);
    }

    #endregion
}
