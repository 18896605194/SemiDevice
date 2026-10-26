using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Drivers.Communication;
using xyz.Drivers.Device;

namespace xyz.Components.Components;

/// <summary>
/// 设备通道的公共部分（machine.xml 里设备下面的通道节点）：管连接和断线重连，只把指令发下去、把回复交给指令；用哪家的解码器由所属设备开机时交给它。
/// 串口、网口各是一个子类（SerialDriverComponent / TcpDriverComponent），只管怎么建字节传输
/// </summary>
public abstract class CommDriverComponent : ComponentBase, ICommDriver
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

    #endregion

    #region Alarm

    [Alarm("设备通讯断开，已停止重连", AlarmCategory.IrrecoverableError,
        AlarmLevel = AlarmLevel.Alarm1,
        Description = "通讯断开后按 EC ReconnectMaxCount 重连了几次都没连上，已停止重连，这一路指令都发不出去",
        Solution = "检查线缆、设备电源和通讯配置（machine.xml 里这个通道节点的串口 / IP / 端口）；好了以后复位，复位会重新开始连")]
    public string ReconnectFailedAlarm = nameof(ReconnectFailedAlarm);

    #endregion

    #region 连接

    private readonly DriverReconnector _reconnector = new();
    private IFrameCodec? _codec;
    private DeviceDriver? _driver;

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
    public static ICommDriver? FindIn(ComponentBase owner)
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
    /// 用哪家的解码器：所属设备开机时交给它（在打开之前）；已经打开了就不能再换成别的
    /// </summary>
    public void UseCodec(IFrameCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        if (_driver is not null && !ReferenceEquals(_codec, codec))
        {
            throw new InvalidOperationException($"{FullPath} 已经按别的解码器打开过，不能再换。");
        }

        _codec = codec;
    }

    /// <summary>
    /// 组件初始化（开机）：启用的建通道（头一次）并打开连接，这一次连没连上，之后断了都由扫描线程按 EC 在后台重连；没启用的什么都不做
    /// </summary>
    public override bool InitComponent()
    {
        bool childrenInitialized = base.InitComponent();
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
                throw new InvalidOperationException($"{FullPath} 没有解码器：挂它的设备要在开机时交给它（UseCodec）。");
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
    /// 关连接（在途指令作废），不再重连；重开走 InitComponent
    /// </summary>
    public void Close()
    {
        _reconnector.Disable();
        _driver?.Close();
    }

    /// <summary>
    /// 扫描（所属设备的扫描线程带着跑）：断了按间隔在后台重连——先关（作废旧连接上的在途指令）再开；重连满 EC 次数还连不上就停下、报警。
    /// 没启用的没建通道，什么都不做
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        if (_driver is null)
        {
            return;
        }

        bool gaveUp = _reconnector.Check(this, _driver.IsConnected, ReconnectIntervalMs, ReconnectMaxCount, () =>
        {
            _driver.Close();
            _driver.Open();
        });
        if (gaveUp)
        {
            RaiseAlarm(ReconnectFailedAlarm);
        }
    }

    /// <summary>
    /// 人工复位：重连次数清零、马上重新开始连（停下来的也接着连），再清本组件的报警
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
