namespace xyz.Drivers.Device;

/// <summary>
/// 一条设备指令（指令模式）：自己组帧、自己认回复、自己取值；通道只管把它发出去、把解好的报文交给它
/// </summary>
public abstract class DeviceCommand
{
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _done = new(false);
    private volatile bool _isCompleted;
    private volatile bool _isSuccess;
    private volatile string _error = string.Empty;

    /// <summary>
    /// 在途键：同一个键同一时间只能有一条在途（回复靠名字对号的协议，同名指令不能并发）
    /// </summary>
    public abstract string Key { get; }

    /// <summary>
    /// 是不是状态轮询发的（状态轮询发之前设上，别处不设）：通道把这条的收发原文记到轮询文件，不进主日志
    /// </summary>
    public bool IsPolling { get; set; }

    /// <summary>
    /// 做完了没有，成功失败都算
    /// </summary>
    public bool IsCompleted => _isCompleted;

    /// <summary>
    /// 做成了没有；没发出去、没做完、没做成都是 false
    /// </summary>
    public bool IsSuccess => _isSuccess;

    /// <summary>
    /// 没做成的原因：设备回的错，或者 Timeout、PortClosed、Aborted 这类作废标记
    /// </summary>
    public string Error => _error;

    /// <summary>
    /// 在不在途：发出去了、还没做完（通道管）
    /// </summary>
    public bool IsInFlight { get; internal set; }

    /// <summary>
    /// 组帧：要发出去的帧体，不带壳（壳由解码器包）
    /// </summary>
    public abstract string BuildFrame();

    /// <summary>
    /// 收到一条报文：是自己的就取值、判做没做完，返回 true；不是自己的返回 false（通道的解析线程上调）
    /// </summary>
    public abstract bool Accept(object message);

    /// <summary>
    /// 等它做完（阻塞）：工具、诊断用，扫描线程里别用，看 IsCompleted
    /// </summary>
    public bool WaitReply(int timeoutMilliseconds)
    {
        return _done.Wait(timeoutMilliseconds);
    }

    /// <summary>
    /// 做成了：取到的值先写好再调
    /// </summary>
    protected void Complete()
    {
        Finish(true, string.Empty);
    }

    /// <summary>
    /// 没做成，带上原因
    /// </summary>
    protected void Fail(string error)
    {
        Finish(false, error);
    }

    /// <summary>
    /// 作废：通道让出它的在途位时调（超时、断线、被中止），之后它的回复再来也没人认
    /// </summary>
    internal void Abandon(string reason)
    {
        Finish(false, reason);
    }

    /// <summary>
    /// 落终态：已经做完的不改（回复和作废可能从两条线程同时来，先到的算）
    /// </summary>
    private void Finish(bool success, string error)
    {
        lock (_gate)
        {
            if (_isCompleted)
            {
                return;
            }

            _error = error;
            _isSuccess = success;
            _isCompleted = true;
        }

        _done.Set();
    }
}
