using xyz.Components.Models;
using xyz.Secs.SecsII;

namespace xyz.Components.Interfaces;

/// <summary>
/// GEM（E30）
/// </summary>
public interface IE30Component
{
    /// <summary>
    /// 在线远程（Host 能下命令）
    /// </summary>
    bool IsRemote { get; }

    /// <summary>
    /// 时间格式（EC TimeFormat）
    /// </summary>
    int TimeFormat { get; }

    /// <summary>
    /// 从在线进了离线
    /// </summary>
    event Action? WentOffline;

    /// <summary>
    /// 报一个事件（S6F11）
    /// </summary>
    void Report(ComponentBase source, string code, params GemData[] data);

    /// <summary>
    /// 发一条设备主动发的报文（S6F1 这类），离线不发
    /// </summary>
    void Send(SecsMessage message);

    /// <summary>
    /// 接到链路上
    /// </summary>
    void Attach(IHsmsComponent link);

    /// <summary>
    /// 从链路上摘下来（宿主退出）
    /// </summary>
    void Detach();
}
