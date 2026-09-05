using xyz.Drivers.Communication;

namespace xyz.Drivers.Loadport.FCD;

/// <summary>
/// FCD LP300 LoadPort 驱动：指令受理与帧路由机制继承自 LoadPortDriverBase，
/// 本类只补 FCD 特有部分——无主 INF 帧的主动事件归一化（PODON/PODOF）。
/// </summary>
public class FcdLoadPortDriver : LoadPortDriverBase
{
    public FcdLoadPortDriver(IFrameCommunication communication) : base(communication)
    {
    }

    /// <summary>
    /// 无主 INF 帧 → 厂商无关主动事件（PODON 放上 / PODOF 拿走）。
    /// </summary>
    protected override LoadPortDeviceEvent? ParseSpontaneousEvent(string body)
    {
        int colon = body.IndexOf(':');
        if (colon <= 0)
        {
            return null;
        }

        string type = body[..colon];
        if (!string.Equals(type, FcdProtocol.Inf, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string rest = body[(colon + 1)..];
        string name = rest.Split('/')[0];
        string content = rest.Length > name.Length ? rest[(name.Length + 1)..] : string.Empty;

        LoadPortDeviceEventKind kind = name.ToUpperInvariant() switch
        {
            "PODON" => LoadPortDeviceEventKind.PodPresent,
            "PODOF" => LoadPortDeviceEventKind.PodRemoved,
            _ => LoadPortDeviceEventKind.Unknown,
        };

        if (kind == LoadPortDeviceEventKind.Unknown)
        {
            return null;
        }

        return new LoadPortDeviceEvent
        {
            Kind = kind,
            VendorEventName = name,
            Content = content,
            RawFrame = body,
            ReceivedAt = DateTime.Now,
        };
    }
}
