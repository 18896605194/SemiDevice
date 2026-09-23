namespace xyz.Drivers.Loadport.FCD.Commands;

public abstract class FcdCommand : LoadPortCommand
{
    /// <summary>
    /// 在途槽位键 = 指令名。
    /// </summary>
    public override string Key => Name;

    /// <summary>
    /// 指令名（VERSN、CLOAD 等），应答按此对号。
    /// </summary>
    protected abstract string Name { get; }

    protected FcdCommand(ILoadPortDriver driver) : base(driver)
    {
    }


    /// <summary>
    /// 是否已收到 ACK。
    /// </summary>
    protected bool AckReceived { get; private set; }

    /// <summary>
    ///  ACK 是否即终结。查询类指令（GET）数据随 ACK 返回且无后续 INF，
    /// </summary>
    protected virtual bool CompleteOnAck => false;

    public override bool ParseMsg(string body)
    {
        int colon = body.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        string type = body[..colon];
        string rest = body[(colon + 1)..];
        string name = rest.Split('/')[0];
        if (!string.Equals(name, Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string data = rest.Length > name.Length ? rest[(name.Length + 1)..] : string.Empty;

        switch (type.ToUpperInvariant())
        {
            case FcdProtocol.Ack:
                AckReceived = true;
                if (CompleteOnAck)
                {
                    Complete(BuildResponse(data));
                }

                return true;

            case FcdProtocol.Inf:
                Complete(BuildResponse(data));
                return true;

            case FcdProtocol.Abs:
                Complete(new LoadPortResponse
                {
                    IsSuccess = false,
                    Error = $"{FcdProtocol.Abs}:{Name}/{data}",
                    Content = data,
                });
                return true;

            case FcdProtocol.Nak:
                Complete(new LoadPortResponse
                {
                    IsSuccess = false,
                    Error = $"{FcdProtocol.Nak}:{Name}/{data}",
                    Content = data,
                });
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// 把终结帧（查询类为 ACK，动作类为 INF）的数据段转换成厂商无关结果；默认成功且只带原文。
    /// 带状态、Mapping 等数据的指令重写，在这里消化 FCD 数据格式；数据不合法时返回失败结果。
    /// </summary>
    protected virtual LoadPortResponse BuildResponse(string data)
    {
        return new LoadPortResponse
        {
            IsSuccess = true,
            Content = data,
        };
    }
}
