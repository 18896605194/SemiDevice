using System.Net;
using System.Net.Sockets;
using xyz.Secs.Diagnostics;

namespace xyz.Secs.Hsms;

/// <summary>
/// 被动监听方（设备端常规模式）：监听端口、每次接受一条 TCP、交给 HsmsSession 等 Select（T7 约束）。
/// HSMS-SS 只允许一条激活连接：已有会话未断时新接入直接关掉并记警告。
/// Accept 循环是同步阻塞体，跑在 LongRunning 专用线程上；Stop 时停监听让 Accept 抛异常退出。
/// </summary>
public sealed class HsmsListener : IDisposable
{
    private const string Category = "Hsms";

    private readonly HsmsSettings _settings;
    private readonly ISecsSink _sink;
    private readonly CancellationTokenSource _stopCts = new();
    private TcpListener? _tcpListener;
    private volatile HsmsSession? _current;
    private int _started;
    private int _disposed;

    /// <summary>每次 Select 成功（含断线重接后）触发一次，业务层在这里重新挂 PrimaryReceived。</summary>
    public event Action<HsmsSession>? SessionEstablished;

    public HsmsSession? Current => _current;

    /// <summary>实际监听地址：Port 配 0 时由系统分配，从这里取回真实端口（冒烟测试用）。</summary>
    public IPEndPoint? LocalEndpoint => _tcpListener?.LocalEndpoint as IPEndPoint;

    public HsmsListener(HsmsSettings settings, ISecsSink? sink = null)
    {
        _settings = settings;
        _sink = sink ?? NullSecsSink.Instance;
    }

    /// <summary>开始监听 sc 配置的端口（IPAddress.Any）。</summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _tcpListener = new TcpListener(IPAddress.Any, _settings.Port);
        _tcpListener.Start();
        _ = Task.Factory.StartNew(AcceptLoop, TaskCreationOptions.LongRunning);
        _sink.Info(Category, $"监听 0.0.0.0:{LocalEndpoint?.Port}，等 EAP 连入");
    }

    private void AcceptLoop()
    {
        while (!_stopCts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = _tcpListener!.AcceptTcpClient();
            }
            catch (Exception exception) when (_stopCts.IsCancellationRequested
                                             || exception is ObjectDisposedException or SocketException)
            {
                // Stop() 停了监听，Accept 会抛这两种，正常退出
                _sink.Info(Category, $"监听停止: {exception.Message}");
                break;
            }

            var existing = _current;
            if (existing is not null && existing.State != HsmsLinkState.NotConnected)
            {
                _sink.Warn(Category, "HSMS-SS 只允许一条连接，新接入被拒绝");
                client.Close();
                continue;
            }

            var session = new HsmsSession(client.GetStream(), _settings, _sink, initiateSelect: false);
            _current = session;
            session.Selected += () =>
            {
                _sink.Info(Category, $"EAP 接入 {client.Client.RemoteEndPoint}，SELECTED");
                SessionEstablished?.Invoke(session);
            };
            session.Closed += _ =>
            {
                if (ReferenceEquals(_current, session))
                {
                    _current = null;
                }

                client.Dispose();
            };
            session.Start();  // 内部起 T7：对端 Select 迟迟不来就断开，端口回到可接状态
        }
    }

    /// <summary>停监听并优雅断开当前会话（发 Separate 再关）。</summary>
    public void Stop()
    {
        _stopCts.Cancel();
        try
        {
            _tcpListener?.Stop();
        }
        catch
        {
            // 监听可能已经停了
        }

        _current?.SendSeparate();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        Stop();
        _stopCts.Dispose();
    }
}
