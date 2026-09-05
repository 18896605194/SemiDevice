using System.IO.Ports;

namespace xyz.Drivers.Communication.Serial;


public abstract class SerialPortBase : ICommunication
{
    private SerialPort? _serialPort;

    #region Properties

    public string PortName => _serialPort?.PortName ?? string.Empty;

    public int BaudRate => _serialPort?.BaudRate ?? 0;

    public Parity Parity => _serialPort?.Parity ?? Parity.None;

    public int DataBits => _serialPort?.DataBits ?? 0;

    public StopBits StopBits => _serialPort?.StopBits ?? StopBits.None;

    public Handshake Handshake
    {
        get => _serialPort?.Handshake ?? Handshake.None;
        set
        {
            if (_serialPort is not null)
            {
                _serialPort.Handshake = value;
            }
        }
    }

    public bool DtrEnable
    {
        get => _serialPort?.DtrEnable ?? false;
        set
        {
            if (_serialPort is not null)
            {
                _serialPort.DtrEnable = value;
            }
        }
    }

    public bool RtsEnable
    {
        get => _serialPort?.RtsEnable ?? false;
        set
        {
            if (_serialPort is not null)
            {
                _serialPort.RtsEnable = value;
            }
        }
    }

    /// <summary>接收等待超时，单位毫秒；有效值遵循 SerialPort.ReadTimeout。</summary>
    public int ReceiveTimeout
    {
        get => _serialPort?.ReadTimeout ?? 0;
        set
        {
            if (_serialPort is not null)
            {
                _serialPort.ReadTimeout = value;
            }
        }
    }

    /// <summary>发送等待超时，单位毫秒；有效值遵循 SerialPort.WriteTimeout。</summary>
    public int SendTimeout
    {
        get => _serialPort?.WriteTimeout ?? 0;
        set
        {
            if (_serialPort is not null)
            {
                _serialPort.WriteTimeout = value;
            }
        }
    }

    /// <summary>每次 Receive 最多读取的字节数，调用方应保证大于零；不是协议包长度，修改后在下一次 Receive 生效。</summary>
    public int ReceiveBufferSize { get; set; } = 4096;

    public virtual bool IsConnected => _serialPort?.IsOpen == true;

    /// <summary>
    /// 已配置的串口，供子类扩展串口控制；不应与 Receive 同时使用其他接收入口。
    /// </summary>
    protected SerialPort Port
    {
        get
        {
            return _serialPort ?? throw new InvalidOperationException("串口尚未创建，请先调用 Create。");
        }
    }

    #endregion

    /// <summary>
    /// 配置串口参数，不在此处打开串口。
    /// </summary>
    public SerialPortBase Create(string portName, int baudRate, string parity, int dataBits, string stopBits)
    {
        _serialPort?.Dispose();
        _serialPort = new SerialPort(portName, baudRate,
            Enum.Parse<Parity>(parity, ignoreCase: true), dataBits,
            Enum.Parse<StopBits>(stopBits, ignoreCase: true))
        {
            ReadTimeout = 1000,
            WriteTimeout = 1000
        };

        return this;
    }

    /// <inheritdoc />
    public virtual void Connect()
    {
        if (!IsConnected)
        {
            Port.Open();
        }
    }

    /// <inheritdoc />
    public virtual void Close()
    {
        _serialPort?.Close();
    }

    /// <inheritdoc />
    public virtual void Send(byte[] data)
    {
        var port = Port;
        try
        {
            port.Write(data, 0, data.Length);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        {
            // 发送失败时可能已有部分数据写出，关闭连接，由上层决定恢复策略。
            Close();
            throw;
        }
    }

    /// <inheritdoc />
    public virtual byte[] Receive()
    {
        var port = Port;
        var buffer = new byte[ReceiveBufferSize];
        int count;
        try
        {
            count = port.Read(buffer, 0, buffer.Length);
        }
        catch (IOException)
        {
            Close();
            throw;
        }

        // 接收超时直接向上传递，不关闭串口，也不调用解析方法。
        Array.Resize(ref buffer, count);
        if (count > 0)
        {
            ParseReceivedData(buffer);
        }

        return buffer;
    }

    protected abstract void ParseReceivedData(byte[] data);

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
