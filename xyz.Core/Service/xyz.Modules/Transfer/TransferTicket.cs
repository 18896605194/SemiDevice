namespace xyz.Modules;

/// <summary>
/// 下单的回执：受理了带单号和"最终结果"任务；没受理带错误码和参数（界面按码查语言包）。
/// 受理只说明校验过了、锁占好了、排上队了，搬没搬成看 <see cref="Completion"/>。
/// </summary>
public sealed class TransferTicket
{
    private TransferTicket(bool accepted, long id, string code, IReadOnlyList<string> args, Task<TransferResult>? completion)
    {
        Accepted = accepted;
        Id = id;
        Code = code;
        Args = args;
        Completion = completion;
    }

    /// <summary>受理了没有。</summary>
    public bool Accepted { get; }

    /// <summary>搬运单号；没受理为 0。</summary>
    public long Id { get; }

    /// <summary>没受理的原因（错误码）；受理了为空。</summary>
    public string Code { get; }

    /// <summary>错误码参数。</summary>
    public IReadOnlyList<string> Args { get; }

    /// <summary>最终结果：设备、站点、晶圆账都做完才完成；没受理为 null。</summary>
    public Task<TransferResult>? Completion { get; }

    public static TransferTicket Accept(long id, Task<TransferResult> completion)
    {
        return new TransferTicket(true, id, string.Empty, [], completion);
    }

    public static TransferTicket Reject(string code, params string[] args)
    {
        return new TransferTicket(false, 0, code, args, null);
    }
}
