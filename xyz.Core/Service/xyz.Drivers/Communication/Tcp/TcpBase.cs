using System.Net.Sockets;

namespace xyz.Drivers.Communication.Tcp;


public abstract class TcpBase : ICommunication
{
    private TcpClient? _client;

    #region Properties

    /// <summary>设备 IP 地址或主机名。</summary>
    public string Host { get; private set; } = string.Empty;

    public int Port { get; private set; }

    /// <summary>接收等待超时，单位毫秒，必须大于零。</summary>
    public int ReceiveTimeout { get; set; } = 1000;

    /// <summary>发送等待超时，单位毫秒，必须大于零。</summary>
    public int SendTimeout { get; set; } = 1000;

    /// <summary>每次 Receive 最多读取的字节数，调用方应保证大于零；不是协议包长度，修改后在下一次 Receive 生效。</summary>
    public int ReceiveBufferSize { get; set; } = 4096;

    /// <summary>最近一次 I/O 的连接状态，不用于判断设备实时在线或业务就绪。</summary>
    public virtual bool IsConnected => _client?.Connected == true;

    /// <summary>已连接的 TCP 客户端，供子类扩展底层通信。</summary>
    protected TcpClient Client
    {
        get
        {
            return _client ?? throw new InvalidOperationException("TCP 尚未连接。");
        }
    }

    #endregion

    /// <summary>
    /// 配置 TCP 连接参数，不在此处建立连接。
    /// </summary>
    public TcpBase Create(string host, int port)
    {
        Host = host;
        Port = port;
        return this;
    }

    /// <inheritdoc />
    public virtual void Connect()
    {
        if (IsConnected)
        {
            return;
        }

        Close();

        var client = new TcpClient();
        try
        {
            client.NoDelay = true;
            client.ReceiveTimeout = ReceiveTimeout;
            client.SendTimeout = SendTimeout;
            client.Connect(Host, Port);
            _client = client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public virtual void Close()
    {
        var client = _client;
        _client = null;
        client?.Dispose();
    }

    /// <inheritdoc />
    public virtual void Send(byte[] data)
    {
        var stream = Client.GetStream();
        try
        {
            stream.Write(data, 0, data.Length);
        }
        catch (IOException exception) when (IsTimeout(exception))
        {
            Close();
            throw new TimeoutException("TCP 发送数据超时；可能已发送部分数据。", exception);
        }
        catch (IOException)
        {
            Close();
            throw;
        }
    }

    /// <inheritdoc />
    public virtual byte[] Receive()
    {
        var stream = Client.GetStream();
        var buffer = new byte[ReceiveBufferSize];
        int count;
        try
        {
            count = stream.Read(buffer, 0, buffer.Length);
        }
        catch (IOException exception) when (IsTimeout(exception))
        {
            // 没有数据不等于断线；允许下一次继续接收。
            throw new TimeoutException("TCP 接收数据超时。", exception);
        }
        catch (IOException)
        {
            Close();
            throw;
        }

        if (count == 0)
        {
            Close();
            return [];
        }

        Array.Resize(ref buffer, count);
        ParseReceivedData(buffer);
        return buffer;
    }

    protected abstract void ParseReceivedData(byte[] data);

    private bool IsTimeout(IOException exception)
    {
        return exception.InnerException is SocketException
        {
            SocketErrorCode: SocketError.TimedOut or SocketError.WouldBlock
        };
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>子类释放额外资源后，应调用 base.Dispose(disposing)。</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }
    }
}
