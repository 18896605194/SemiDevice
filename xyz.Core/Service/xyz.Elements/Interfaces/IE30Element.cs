using xyz.Secs.Gem.E30;
using xyz.Elements.Attributes;

namespace xyz.Elements.Interfaces;

/// <summary>
/// GEM（E30）
/// </summary>
[Ioc]
public interface IE30Element
{
    /// <summary>
    /// 在线远程（Host 能下命令）
    /// </summary>
    bool IsRemote { get; }

    /// <summary>
    /// GEM 的协议部分（xyz.Secs）：别的标准接链路时要它；没接链路（EAP 没启用）为 null
    /// </summary>
    E30Engine? Engine { get; }

    /// <summary>
    /// 接到链路上
    /// </summary>
    void Attach(IHsms link);

    /// <summary>
    /// 从链路上摘下来（宿主退出）
    /// </summary>
    void Detach();
}
