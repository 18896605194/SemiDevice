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
    private readonly object _sessionGate = new();

    /// <summary>
    /// 当前这一轮接收泵；没打开或已关为 null。每打开一次起一轮新的，关的时候只停当前这一轮。
    /// 不用一个全局"在收"标志：断线重连时旧泵可能在新连接起来以后才醒（卡在收包上），
    /// 它退出时只能停自己那一轮，不能把新的一轮也停了。
    /// </summary>
    private PumpSession? _session;

    /// <summary>一轮接收泵的停止标记。</summary>
    private sealed class PumpSession
    {
        public volatile bool IsStopped;
    }

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
    /// 打开连接并起一轮接收泵；已经连着、泵也在收就直接返回。可重复调（断线后重连就是再调一次）。
    /// </summary>
    public bool Open()
    {
        try
        {
            lock (_sessionGate)
            {
                var current = _session;
                if (IsConnected && current is not null && !current.IsStopped)
                {
                    return true;
                }

                _transport.Connect();

                // 上一轮（断线留下的）作废，起新的一轮。
                if (current is not null)
                {
                    current.IsStopped = true;
                }

                var session = new PumpSession();
                _session = session;
                Task.Factory.StartNew(() => PumpLoop(session), TaskCreationOptions.LongRunning);
                return IsConnected;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 停掉当前这一轮接收泵并关闭连接。
    /// </summary>
    public void Close()
    {
        lock (_sessionGate)
        {
            var session = _session;
            _session = null;
            if (session is not null)
            {
                session.IsStopped = true;
            }

            _transport.Close();
        }
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

    private void PumpLoop(PumpSession session)
    {
        while (!session.IsStopped)
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
                // 传输故障：这一轮泵退出（只停自己），由上层重新 Open 起新的一轮。
                session.IsStopped = true;
                return;
            }

            // 收的时候这一轮已经被关了：这批数据不归它（可能已经是新连接的），丢掉退出。
            if (session.IsStopped)
            {
                return;
            }

            // TCP 对端正常关闭时返回空且连接已断：安静退出泵。
            if (data.Length == 0)
            {
                if (!IsConnected)
                {
                    session.IsStopped = true;
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
