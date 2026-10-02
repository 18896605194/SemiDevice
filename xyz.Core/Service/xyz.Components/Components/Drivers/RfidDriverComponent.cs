using System.Diagnostics;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Drivers.Communication;
using xyz.Drivers.Rfid;

namespace xyz.Components.Components;

/// <summary>
/// 一次读码的结果。
/// </summary>
/// <param name="IsSuccess">是否读到。</param>
/// <param name="CarrierId">载具 ID，失败时为空。</param>
/// <param name="Error">失败原因，成功时为空。</param>
public sealed record RfidReadResult(bool IsSuccess, string CarrierId, string Error)
{
    public static RfidReadResult Success(string carrierId)
    {
        return new RfidReadResult(true, carrierId, string.Empty);
    }

    public static RfidReadResult Failure(string error)
    {
        return new RfidReadResult(false, string.Empty, error);
    }
}

/// <summary>
/// RFID 读头驱动组件基座：传输配置、读码步进机（非阻塞）。
/// 品牌只补两件事——建驱动、建读码指令；sc.xml 换品牌壳 Type 即换品牌，机型代码不动。
///
/// 读码是**非阻塞**的：BeginRead 只发起，结果由所属模块在扫描周期里 TakeResult 取。
/// 不做成同步返回是因为读头收发要走好几轮握手（几百毫秒），而模块扫描线程 50ms 一拍，
/// 阻塞在这儿会把整个 LoadPort 的轮询连同在途操作一起卡住。
///
/// 本组件挂在 LoadPort 模块下面，OnScan 由父模块的扫描线程递归带着跑，不自己起线程。
/// </summary>
public abstract class RfidDriverComponent : ComponentBase
{
    #region SC

    [SCEditor("Serial", "RFID", "通讯类型：Serial=串口，Tcp=网口")]
    public CommType CommType { get; set; } = CommType.Serial;

    [SCEditor("COM", "RFID", "串口名称（CommType=Serial 时生效）")]
    public string PortName { get; set; } = string.Empty;

    [SCEditor("9600", "RFID", "串口波特率（CommType=Serial 时生效）")]
    public int BaudRate { get; set; } = 9600;

    [SCEditor("None", "RFID", "串口校验位（CommType=Serial 时生效）")]
    public string Parity { get; set; } = "None";

    [SCEditor("8", "RFID", "串口数据位（CommType=Serial 时生效）")]
    public int DataBits { get; set; } = 8;

    [SCEditor("One", "RFID", "串口停止位（CommType=Serial 时生效）")]
    public string StopBits { get; set; } = "One";

    [SCEditor("127.0.0.1", "RFID", "网口 IP（CommType=Tcp 时生效）")]
    public string Host { get; set; } = "127.0.0.1";

    [SCEditor("7090", "RFID", "网口端口（CommType=Tcp 时生效）")]
    public int NetPort { get; set; } = 7090;

    [SCEditor("0", "RFID", "载具 ID 在标签内存里的起始偏移")]
    public int IdStart { get; set; }

    [SCEditor("16", "RFID", "载具 ID 字节数")]
    public int IdLength { get; set; } = 16;

    #endregion

    #region EC

    [VariableMark(VariableType.EC, ValueFormat.Int, unit: "ms", min: "1000", max: "300000",
        @default: "5000", description: "读码超时")]
    public int ReadCarrierIdTimeout
    {
        get { return GetEcInt(nameof(ReadCarrierIdTimeout)); }
        set { SetEcInt(nameof(ReadCarrierIdTimeout), value); }
    }

    #endregion

    #region 驱动连接

    public IRfidDriver? Driver { get; private set; }

    public bool IsConnected
    {
        get { return Driver?.IsConnected ?? false; }
    }

    /// <summary>
    /// 建驱动（首次）并打开连接。可重复调用：已建好的驱动只重开连接。
    /// </summary>
    public bool Open()
    {
        var driver = Driver;
        if (driver is null)
        {
            driver = CreateDriver();
            Driver = driver;
        }

        return driver.Open();
    }

    /// <summary>关闭连接；驱动保留，重开走 Open。</summary>
    public void Close()
    {
        Driver?.Close();
    }

    /// <summary>按 sc.xml 配的 CommType 建传输（串口/网口都吃配置）。</summary>
    protected ICommunication CreateTransport()
    {
        return CommunicationFactory.Create(CommType, PortName, BaudRate, Parity, DataBits, StopBits, Host, NetPort);
    }

    #endregion

    #region 读码（非阻塞步进机）

    private readonly object _gate = new();
    private readonly Stopwatch _watch = new();

    private RfidCommand? _reading;
    private RfidReadResult? _result;

    public bool IsReading
    {
        get
        {
            lock (_gate)
            {
                return _reading is not null;
            }
        }
    }

    /// <summary>
    /// 发起一次读码：只提交指令就返回，结果在后续扫描周期里出。
    /// 任意线程可调（RPC 线程发起、扫描线程收结果）。未连接或上一次还没读完返回 false。
    /// </summary>
    public bool BeginRead()
    {
        var driver = Driver;
        if (driver is null || !driver.IsConnected)
        {
            return false;
        }

        lock (_gate)
        {
            if (_reading is not null)
            {
                return false;
            }

            var command = CreateReadCarrierIdCommand();
            if (!driver.Submit(command))
            {
                return false;
            }

            _reading = command;
            _watch.Restart();
            return true;
        }
    }

    /// <summary>
    /// 取走最近一次读码结果；还没出结果返回 null。取走即清，同一个结果只会拿到一次。
    /// </summary>
    public RfidReadResult? TakeResult()
    {
        lock (_gate)
        {
            var result = _result;
            _result = null;
            return result;
        }
    }

    /// <summary>
    /// 扫描周期：推进读码步进机（父模块的扫描线程带着跑）。
    /// </summary>
    protected override void OnScan()
    {
        base.OnScan();
        ScanRead();
    }

    private void ScanRead()
    {
        lock (_gate)
        {
            if (_reading is not { } command)
            {
                return;
            }

            if (command.IsCompleted)
            {
                var response = command.Response;
                _result = response is { IsSuccess: true }
                    ? RfidReadResult.Success(response.CarrierId ?? string.Empty)
                    : RfidReadResult.Failure(response?.Error ?? "NoResponse");
                _reading = null;
                _watch.Reset();
                return;
            }

            int timeout = ReadCarrierIdTimeout;
            if (_watch.ElapsedMilliseconds < timeout)
            {
                return;
            }

            // 超时必须让出驱动的在途位，否则这条指令永远占着，之后再也读不了。
            Driver?.AbandonInflight("Timeout");
            _result = RfidReadResult.Failure($"读码超时（{timeout}ms）");
            _reading = null;
            _watch.Reset();
            LogHelper.Warn(Name, $"读码超时（{timeout}ms）");
        }
    }

    #endregion

    #region 品牌实现（造哪条指令、走什么传输，由品牌壳填）

    /// <summary>造品牌驱动（传输 + 品牌帧编解码 + 驱动）。</summary>
    protected abstract IRfidDriver CreateDriver();

    /// <summary>造品牌的读码指令（吃 IdStart/IdLength 配置）。</summary>
    protected abstract RfidCommand CreateReadCarrierIdCommand();

    #endregion
}
