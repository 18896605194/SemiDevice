using System.Net.Sockets;
using xyz.Secs.Diagnostics;

namespace xyz.Secs.Hsms;

/// <summary>
/// 主动连出方：后台循环 连接 → Select（Session 内自动做）→ SELECTED 宣告 → 断开后等 T5 重试。
/// EAP 侧或要求设备主动连出的场合用这个；设备端常规用 HsmsListener。
/// 连接循环是同步阻塞体，跑在 LongRunning 专用线程上（与 HsmsSession 的泵同款线程模型）。
/// </summary>
public sealed class HsmsConnector : IDisposable
{
    private const string Category = "Hsms";
    private const int ConnectTimeoutMs = 10_000;

    private readonly HsmsSettings _settings;
    private readonly ISecsSink _sink;
    private readonly CancellationTokenSource _stopCts = new();
    private volatile HsmsSession? _current;
    private int _started;
    private int _disposed;

    /// <summary>每次 Select 成功（含重连后）触发一次，业务层在这里重新挂 PrimaryReceived。</summary>
    public event Action<HsmsSession>? SessionEstablished;

    public HsmsSession? Current => _current;

    public HsmsConnector(HsmsSettings settings, ISecsSink? sink = null)
    {
        _settings = settings;
        _sink = sink ?? NullSecsSink.Instance;
    }

    public void Start()
    {
        _settings.Validate();
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _ = Task.Factory.StartNew(ConnectLoop, TaskCreationOptions.LongRunning);
    }

    private void ConnectLoop()
    {
        while (!_stopCts.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = new TcpClient();
                // 阻塞等连接完成（跑在专用线程上）；停止时关 socket 把连接打断
                using (_stopCts.Token.Register(() => client.Close()))
                {
                    if (!client.ConnectAsync(_settings.Host, _settings.Port).Wait(ConnectTimeoutMs))
                    {
                        throw new TimeoutException("连接超时");
                    }
                }

                var session = new HsmsSession(client.GetStream(), _settings, _sink, initiateSelect: true);
                _current = session;
                session.Selected += () =>
                {
                    _sink.Info(Category, $"已连上 {_settings.Host}:{_settings.Port}");
                    SessionEstablished?.Invoke(session);
                };
                session.Closed += reason =>
                {
                    if (ReferenceEquals(_current, session))
                    {
                        _current = null;
                    }

                    client.Dispose();
                };
                session.Start();
                WaitUntilClosed(session);
            }
            catch (Exception exception)
            {
                _sink.Warn(Category, $"连接 {_settings.Host}:{_settings.Port} 失败: {exception.Message}");
                client?.Dispose();
            }

            // T5：断线后的重连间隔（可被 Stop 打断）
            if (_stopCts.Token.WaitHandle.WaitOne(_settings.T5ConnectRetryMs))
            {
                break;
            }
        }
    }

    /// <summary>阻塞等会话关闭。停止路径上 Stop 会先对当前会话发 Separate（必然走到 Closed），不会卡死。</summary>
    private static void WaitUntilClosed(HsmsSession session)
    {
        using var closed = new ManualResetEventSlim(false);
        session.Closed += _ => closed.Set();
        if (session.State == HsmsLinkState.NotConnected)
        {
            return;
        }

        closed.Wait();
    }

    /// <summary>停循环并优雅断开当前会话（发 Separate 再关）。</summary>
    public void Stop()
    {
        _stopCts.Cancel();
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
