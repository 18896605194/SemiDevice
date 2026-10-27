using xyz.Elements.Attributes;
using xyz.Elements.Interfaces;
using xyz.Secs.Gem.E39;

namespace xyz.Elements.Elements;

/// <summary>
/// 对象服务（SEMI E39，machine.xml 的 Eap 下的 E39 节点）：Host 用 S14 查、改、建、删各标准登记的对象。
/// 协议在 xyz.Secs 的 E39Engine；E90、E40、E94 接链路时把自己的对象类型登记进去，EDA 按类型名找对象
/// </summary>
[Element(description: "对象服务（SEMI E39）：Host 用 S14 查、改、建、删各标准登记的对象")]
public class E39Element : ElementBase, IE39Element
{
    /// <summary>
    /// 对象服务的协议部分；没接链路为 null
    /// </summary>
    public E39Engine? Engine { get; private set; }

    /// <summary>
    /// 接到链路上：登记 S14 的处理方（EAP 元件在链路打开之前调）
    /// </summary>
    public void Attach(IHsms link, IE30Element gem)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(gem);
        var engine = gem.Engine;
        if (engine is null)
        {
            throw new InvalidOperationException($"{Name}：E30 还没接链路");
        }

        Engine = new E39Engine(link.Link, engine, new SecsLog(this), Name);
    }
}
