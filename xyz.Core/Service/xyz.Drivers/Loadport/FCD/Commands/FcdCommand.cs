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

    protected FcdCommand(LoadPortDriverBase driver) : base(driver)
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
                OnAck(data);
                if (CompleteOnAck && !IsCompleted)
                {
                    IsCompleted = true;
                    IsSucceeded = true;
                }

                return true;

            case FcdProtocol.Inf:
                OnInf(data);
                IsCompleted = true;
                IsSucceeded = true;
                return true;

            case FcdProtocol.Abs:
                OnAbs(data);
                IsCompleted = true;
                IsSucceeded = false;
                Error = $"{FcdProtocol.Abs}:{Name}/{data}";
                return true;

            case FcdProtocol.Nak:
                IsCompleted = true;
                IsSucceeded = false;
                Error = $"{FcdProtocol.Nak}:{Name}/{data}";
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// 收到 ACK；查询类在此取数据并置终态。
    /// </summary>
    protected virtual void OnAck(string data)
    {
    }

    /// <summary>
    /// 收到 INF（动作完成通知）。
    /// </summary>
    protected virtual void OnInf(string data)
    {
    }

    /// <summary>
    /// 收到 ABS（异常完成）。
    /// </summary>
    protected virtual void OnAbs(string data)
    {
    }
}
