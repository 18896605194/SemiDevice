using System.Text;

namespace xyz.Drivers.Communication;

/// <summary>
/// 通信的 发送接收的公共层，具体的ICommunication 又这个转发
/// </summary>
public class FrameCommunication : IFrameCommunication, IDisposable
{
    #region 字段与构造

    /// <summary>
    /// 串口和网口的接口，这里目前不确定
    /// </summary>
    private readonly ICommunication _transport;
    private readonly IFrameCodec _codec;
    private readonly Encoding _encoding;
    private readonly object _sendGate = new();
    private volatile bool _pumping;

    /// <summary>
    /// 收到一条完整帧体（已去壳），在接收泵线程触发；订阅方应及时返回，异常不会拖垮接收泵。
    /// </summary>
    public event Action<string>? FrameReceived;

    /// <param name="transport">字节传输（串口/网口）。</param>
    /// <param name="codec">帧编解码，自持缓冲，每帧通讯独占一个实例。</param>
    /// <param name="encoding">帧文本编码，默认 ASCII。</param>
    public FrameCommunication(ICommunication transport, IFrameCodec codec, Encoding? encoding = null)
    {
        _transport = transport;
        _codec = codec;
        _encoding = encoding ?? Encoding.ASCII;
    }

    #endregion

    #region 连接

    public bool IsConnected => _transport.IsConnected;

    /// <summary>
    /// 打开连接并启动接收泵。
    /// </summary>
    public bool Open()
    {
        try
        {
            if (IsConnected)
            {
                return true;
            }

            _transport.Connect();

            if (!_pumping)
            {
                _pumping = true;
                Task.Factory.StartNew(PumpLoop, TaskCreationOptions.LongRunning);
            }

            return IsConnected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 停止接收泵并关闭连接。
    /// </summary>
    public void Close()
    {
        _pumping = false;
        _transport.Close();
    }

    #endregion

    #region 收发

    /// <summary>
    /// 发送一条帧体（由编解码统一包壳），内部串行化，多线程调用安全。
    /// </summary>
    public void Send(string body)
    {
        lock (_sendGate)
        {
            _transport.Send(_encoding.GetBytes(_codec.Wrap(body)));
        }
    }

    #endregion

    #region 接收泵

    private void PumpLoop()
    {
        while (_pumping)
        {
            byte[] data;
            try
            {
                data = _transport.Receive();
            }
            catch (TimeoutException)
            {
                // 轮询超时：正常空闲，继续。
                continue;
            }
            catch
            {
                // 传输故障：泵退出，由上层决定恢复（重新 Open 会起新泵）。
                _pumping = false;
                return;
            }

            // TCP 对端正常关闭时返回空且连接已断：安静退出泵。
            if (data.Length == 0)
            {
                if (!IsConnected)
                {
                    _pumping = false;
                }

                continue;
            }

            foreach (string frame in _codec.Extract(_encoding.GetString(data)))
            {
                try
                {
                    FrameReceived?.Invoke(frame);
                }
                catch
                {
                    // 订阅方异常不拖垮接收泵。
                }
            }
        }
    }

    #endregion

    public void Dispose()
    {
        Close();
    }
}
