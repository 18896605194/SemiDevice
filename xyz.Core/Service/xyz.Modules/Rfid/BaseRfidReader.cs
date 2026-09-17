using System.Diagnostics;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Drivers.Communication;
using xyz.Drivers.Rfid;

namespace xyz.Modules;

/// <summary>
/// RFID 读头组件基类：装机常量、连接、以及非阻塞的读码步进机。
/// 品牌只补两件事——建驱动、建读码指令；其余时序与超时都在这里。
///
/// 本组件挂在 LoadPort 模块下面，OnScan 由父模块的扫描线程递归带着跑，不自己起线程。
/// </summary>
public abstract class BaseRfidReader : ComponentBase, IRfidReader
{
    #region SC 装机常量

    [SCEditor("", "RFID", "RFID 读头品牌")]
    public string Brand { get; set; } = string.Empty;

    [SCEditor("Serial", "RFID", "通讯类型：Serial=串口，Tcp=网口")]
    public CommType CommType { get; set; } = CommType.Serial;

    [SCEditor("COM", "RFID", "RFID 读头串口名称（CommType=Serial 时生效）")]
    public string PortName { get; set; } = string.Empty;

    [SCEditor("9600", "RFID", "RFID 读头串口波特率（CommType=Serial 时生效）")]
    public int BaudRate { get; set; } = 9600;

    [SCEditor("None", "RFID", "RFID 读头串口校验位（CommType=Serial 时生效）")]
    public string Parity { get; set; } = "None";

    [SCEditor("8", "RFID", "RFID 读头串口数据位（CommType=Serial 时生效）")]
    public int DataBits { get; set; } = 8;

    [SCEditor("One", "RFID", "RFID 读头串口停止位（CommType=Serial 时生效）")]
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

    /// <summary>
    /// 按 sc.xml 配的 CommType 建传输。
    /// </summary>
    protected ICommunication CreateTransport()
    {
        return CommunicationFactory.Create(CommType, PortName, BaudRate, Parity, DataBits, StopBits, Host, NetPort);
    }

    /// <summary>
    /// 创建品牌驱动（传输 + 品牌帧编解码 + 驱动）；机型组件重写。
    /// </summary>
    protected virtual IRfidDriver CreateDriver()
    {
        throw new NotSupportedException($"{GetType().Name} 尚未实现 CreateDriver。");
    }

    /// <summary>
    /// 创建品牌的读码指令；机型组件重写。
    /// </summary>
    protected virtual RfidCommand CreateReadCarrierIdCommand()
    {
        throw new NotSupportedException($"{GetType().Name} 尚未实现 CreateReadCarrierIdCommand。");
    }

    public bool Open()
    {
        Driver = CreateDriver();
        return Driver.Open();
    }

    public void Close()
    {
        Driver?.Close();
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
    /// 任意线程可调（RPC 线程发起、扫描线程收结果）。
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
    /// 取走最近一次读码结果；取走即清。
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
}
