namespace xyz.Drivers.Robot;

public abstract class RobotCommand
{
    /// <summary>
    /// 指令唯一键（如锐洁的回显名）。驱动按它分在途槽位：同键指令同一时间只能有一条在途
    /// </summary>
    public abstract string Key { get; }

    /// <summary>
    /// 提交口（驱动基类），构造时注入
    /// </summary>
    protected RobotDriverBase Driver { get; }

    private volatile RobotResponse? _response;
    private volatile bool _isCompleted;
    private readonly ManualResetEventSlim _replied = new(false);

    /// <summary>
    /// 是否最终完成，失败成功都算。先写入结果，再置为 true，供等待方和扫描线程读取。
    /// </summary>
    public bool IsCompleted => _isCompleted;

    /// <summary>
    /// 指令结果（厂商无关），到终态前为 null。上层只读本对象，不碰品牌指令的内部字段。
    /// </summary>
    public RobotResponse? Response => _response;

    /// <summary>
    /// 是否在途：已发出且未到终态。就是还在字典里面
    /// </summary>
    public bool IsInFlight { get; internal set; }

    /// <summary>
    /// 是否运动类指令：设备急停后不再回被打断的运动结果，由驱动在停止确认后以失败终结。
    /// </summary>
    public virtual bool IsMotion => false;

    protected RobotCommand(RobotDriverBase driver)
    {
        Driver = driver ?? throw new ArgumentNullException(nameof(driver));
    }

    /// <summary>
    /// 落终态：先写结果，再置完成并唤醒 WaitReply 的等待方；已完成时忽略（保留首个终态）。
    /// 品牌指令解析到终结帧时调用（在驱动路由消费任务上，单线程）。
    /// </summary>
    protected void Complete(RobotResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (_isCompleted)
        {
            return;
        }

        _response = response;
        _isCompleted = true;
        _replied.Set();
    }

    /// <summary>
    /// 驱动判定本指令已被打断、设备不会再回结果时调用：以失败终结（在驱动路由消费任务上）。
    /// </summary>
    internal void Interrupt(string reason)
    {
        Complete(new RobotResponse
        {
            IsSuccess = false,
            Error = reason,
        });
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
    /// 解析一帧已去壳的回复体；返回 true 表示这一帧属于本指令（按回显名对上号）。
    /// 解析到终结帧时把厂商数据转换成 RobotResponse 并调用 Complete。
    /// </summary>
    public abstract bool ParseMsg(string body);
}
