namespace xyz.Secs;

/// <summary>SECS/HSMS 类库内所有异常的基类。</summary>
public class SecsException : Exception
{
    public SecsException(string message) : base(message)
    {
    }
}

/// <summary>事务超时：T3（数据消息等回复）或 T6（Select/Linktest 等控制事务）到期。</summary>
public sealed class SecsTimeoutException : SecsException
{
    public SecsTimeoutException(string message) : base(message)
    {
    }
}

/// <summary>连接不可用：未连接、未 SELECTED、或发送途中断线；在途事务作废时也抛这个。</summary>
public sealed class HsmsConnectionException : SecsException
{
    public HsmsConnectionException(string message) : base(message)
    {
    }
}
