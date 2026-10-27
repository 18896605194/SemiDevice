using xyz.Secs.Gem.E90;

namespace xyz.Elements.Interfaces;

/// <summary>
/// E90（片跟踪）
/// </summary>
public interface IE90Element
{
    /// <summary>
    /// 接到对象服务和晶圆账上（事件经 RaiseEvent 报，不用接 GEM）
    /// </summary>
    void Attach(IE39Element? objects, ILedgerManager ledger, bool waitForCarrier);

    /// <summary>
    /// 从设备上摘下来（宿主退出）
    /// </summary>
    void Detach();

    /// <summary>
    /// 片跟踪的协议部分（xyz.Secs）：E87 槽图认定了通知它建片对象；没接为 null
    /// </summary>
    E90Engine? Engine { get; }
}
