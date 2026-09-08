namespace xyz.Drivers.Loadport;

public abstract class LoadPortCommand
{
    /// <summary>
    /// 指令唯一键（如 FCD 的指令名）。驱动按它分在途槽位： 不同指令互不干扰
    /// </summary>
    public abstract string Key { get; }

    /// <summary>
    /// 提交口（驱动基类），构造时注入
    /// </summary>
    protected LoadPortDriverBase Driver { get; }

    /// <summary>
    /// 是否最终完成，失败成功都算。先写入结果，再置为 true，供等待方和扫描线程读取。
    /// </summary>
    public bool IsCompleted
    {
        get => _isCompleted;
        protected set
        {
            _isCompleted = value;
            if (value)
            {
                // 唤醒 WaitReply 的等待方。
                _replied.Set();
            }
        }
    }

    private volatile bool _isCompleted;
    private readonly ManualResetEventSlim _replied = new(false);

    /// <summary>
    /// 是否在途：已发出且未到终态。就是还在字典里面
    /// </summary>
    public bool IsInFlight { get; internal set; }

    /// <summary>
    /// 成功完成
    /// </summary>
    public bool IsSucceeded { get; protected set; }

    /// <summary>
    /// 失败原因
    /// </summary>
    public string Error { get; protected set; } = string.Empty;

    protected LoadPortCommand(LoadPortDriverBase driver)
    {
        Driver = driver ?? throw new ArgumentNullException(nameof(driver));
    }

    /// <summary>
    /// 提交自己：经驱动占在途槽位并下发，立即返回。子类可重写以定制提交流程。
    /// </summary>
    public virtual bool Execute()
    {
        return Driver.Submit(this);
    }

    /// <summary>
    /// 等待回复到终态（阻塞，事件唤醒不空转）。
    /// 用于工具/诊断/初始化等同步场景；模块扫描线程正常应轮询 IsCompleted，不要用此方法。
    /// </summary>
    public bool WaitReply(int timeoutMilliseconds)
    {
        return _replied.Wait(timeoutMilliseconds);
    }

    /// <summary>
    /// 组装发送报文体,没有头和尾
    /// </summary>
    public abstract string BuildMsg();

    /// <summary>
    /// 解析一帧已去壳的回复体；返回 true 表示这一帧属于本指令（按指令名对上号）。
    /// </summary>
    public abstract bool ParseMsg(string body);
}
