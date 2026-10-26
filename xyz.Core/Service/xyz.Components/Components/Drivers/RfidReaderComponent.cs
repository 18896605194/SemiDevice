using System.Diagnostics;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Drivers.Communication;
using xyz.Drivers.Device;
using xyz.Shared.Dtos;

namespace xyz.Components.Components;

/// <summary>
/// RFID 读头：管读码流程和读码超时，指令经下面挂的 Driver 通道发；用哪家的解码器、读码发哪条指令由项目里的读头类给
/// </summary>
public abstract class RfidReaderComponent<TReadCommand> : ComponentBase, ICarrierIdReader where TReadCommand : DeviceCommand
{
    #region Machine

    [Machine("0", "RFID", "载具 ID 在标签内存里的起始偏移")]
    public int IdStart { get; set; }

    [Machine("16", "RFID", "载具 ID 字节数")]
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

    #region 通道

    private ICommDriver? _driver;
    private IFrameCodec? _codec;

    /// <summary>
    /// 下面挂的通道连上了没有
    /// </summary>
    public bool IsConnected
    {
        get { return _driver?.IsConnected ?? false; }
    }

    /// <summary>
    /// 组件初始化（开机）：认下面挂的通道、把解码器交给它，再由基类初始化子组件（通道在它自己的初始化里连）；没挂通道开机就报
    /// </summary>
    public override bool InitComponent()
    {
        var driver = CommDriverComponent.FindIn(this);
        if (driver is null)
        {
            throw new InvalidOperationException($"读头 {FullPath} 的 machine.xml 节点下没有启用的通道（串口 SerialDriverComponent 或网口 TcpDriverComponent，IsEnable=True）。");
        }

        _codec ??= CreateCodec();
        driver.UseCodec(_codec);
        _driver = driver;
        return base.InitComponent();
    }

    /// <summary>
    /// 关连接，不再重连
    /// </summary>
    public void Close()
    {
        _driver?.Close();
    }

    #endregion

    #region 读码（非阻塞步进机）

    private readonly object _gate = new();
    private readonly Stopwatch _watch = new();

    private TReadCommand? _reading;
    private HandleResult<string>? _result;

    /// <summary>
    /// 正在读码：发出去了还没出结果
    /// </summary>
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
    /// 发起一次读码：只把指令发出去就返回，结果在后面的扫描里出。任意线程可调；没连上或上一次还没读完返回 false
    /// </summary>
    public bool StartReadCarrierId()
    {
        if (_driver is null || !_driver.IsConnected)
        {
            return false;
        }

        lock (_gate)
        {
            if (_reading is not null)
            {
                return false;
            }

            var command = CreateReadCommand();
            if (!_driver.Send(command))
            {
                return false;
            }

            _reading = command;
            _watch.Restart();
            return true;
        }
    }

    /// <summary>
    /// 取走最近一次读码结果；还没出结果返回 null。取走就清，同一个结果只拿得到一次
    /// </summary>
    public HandleResult<string>? GetCarrierIdResult()
    {
        lock (_gate)
        {
            var result = _result;
            _result = null;
            return result;
        }
    }

    /// <summary>
    /// 扫描（所属 LoadPort 的扫描线程带着跑）：推进读码，通道的重连由通道自己的扫描做
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
            var command = _reading;
            if (command is null)
            {
                return;
            }

            if (command.IsCompleted)
            {
                _result = ToResult(command);
                _reading = null;
                _watch.Reset();
                return;
            }

            int timeout = ReadCarrierIdTimeout;
            if (_watch.ElapsedMilliseconds < timeout)
            {
                return;
            }

            // 超时一定要让出在途位，不然这条一直占着，之后再也读不了
            _driver?.Abandon(command, "Timeout");
            _result = HandleResult<string>.Fail($"读码超时（{timeout}ms）");
            _reading = null;
            _watch.Reset();
            LogHelper.Warn(this, $"读码超时（{timeout}ms）");
        }
    }

    /// <summary>
    /// 读码指令做完了转成结果：读到了 Result 是载具号，没读到 ErrorMessage 是原因（不能是空串，空的会被当成读到了）
    /// </summary>
    private HandleResult<string> ToResult(TReadCommand command)
    {
        if (command.IsSuccess)
        {
            return HandleResult<string>.Success(CarrierIdOf(command));
        }

        if (string.IsNullOrEmpty(command.Error))
        {
            return HandleResult<string>.Fail("读头回了失败，没带原因");
        }

        return HandleResult<string>.Fail(command.Error);
    }

    #endregion

    #region 项目里的读头类填

    /// <summary>
    /// 这家读头的解码器
    /// </summary>
    protected abstract IFrameCodec CreateCodec();

    /// <summary>
    /// 读码发哪条指令（吃 IdStart / IdLength）
    /// </summary>
    protected abstract TReadCommand CreateReadCommand();

    /// <summary>
    /// 读码指令做成了，从它身上取载具号
    /// </summary>
    protected abstract string CarrierIdOf(TReadCommand command);

    #endregion
}
