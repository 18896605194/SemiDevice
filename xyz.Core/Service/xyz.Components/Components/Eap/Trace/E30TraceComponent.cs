using xyz.Components.Attributes;
using xyz.Components.Interfaces;
using xyz.Secs.Gem.Trace;

namespace xyz.Components.Components;

/// <summary>
/// Trace 数据采集（S2F23 / S6F1，machine.xml 的 Eap 下的 Trace 节点）：按 Host 指定的周期采 SV，攒够一包发给 Host。
/// 协议在 xyz.Secs 的 E30TraceEngine，SV 按 GEM 的编号表读
/// </summary>
[Component(description: "Trace 数据采集（S2F23 / S6F1）：按 Host 指定的周期采 SV 发给 Host")]
public class E30TraceComponent : ComponentBase, IE30TraceComponent
{
    private E30TraceEngine? _engine;

    /// <summary>
    /// 接到链路和 GEM 上（EAP 组件在链路打开之前调）：登记 S2F23，订离线
    /// </summary>
    public void Attach(IHsms link, IE30Component gem)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(gem);
        var engine = gem.Engine;
        if (engine is null)
        {
            throw new InvalidOperationException($"{Name}：E30 还没接链路");
        }

        _engine = new E30TraceEngine(link.Link, engine, new SecsLog(this), Name);
    }

    /// <summary>
    /// 从 GEM 上摘下来，停掉全部 Trace（宿主退出）
    /// </summary>
    public void Detach()
    {
        _engine?.Stop();
        _engine = null;
    }
}
