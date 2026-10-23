namespace xyz.Drivers.Device;

/// <summary>
/// 认一种报文的指令：品牌的指令都继承它，拿到的就是自己品牌解码器解好的报文
/// </summary>
public abstract class DeviceCommand<TMessage> : DeviceCommand where TMessage : class
{
    /// <summary>
    /// 不是本品牌的报文一律不认，是的交给 Handle
    /// </summary>
    public sealed override bool Accept(object message)
    {
        if (message is TMessage typed)
        {
            return Handle(typed);
        }

        return false;
    }

    /// <summary>
    /// 收到一条本品牌的报文：比是不是自己的，是就取值、判做没做完（Complete / Fail），返回 true
    /// </summary>
    protected abstract bool Handle(TMessage message);
}
