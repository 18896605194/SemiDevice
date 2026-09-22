using xyz.Components.Attributes;

namespace xyz.Components.Components;

/// <summary>
/// 阀门组件：单 DO 驱动、可选到位反馈——IO 点位与到位判定都在 <see cref="OneStateComponent"/>。
/// 通电开、断电靠弹簧回关位，关那一侧不受控。这儿只把通/断电叫成开/关。
/// </summary>
[Component(description: "阀门组件 (单 DO 驱动，可选到位反馈)")]
public class ValveComponent : OneStateComponent
{
    /// <summary>开阀（通电）；返回 true 只表示 DO 已写进 PLC，到位看 ActionState。</summary>
    public bool Open()
    {
        return On();
    }

    /// <summary>关阀（断电回位），写完即完成。</summary>
    public bool Close()
    {
        return Off();
    }

    /// <summary>是否开着：接了到位 DI 看 DI，没接看 DO 回读。</summary>
    public bool IsOpened => IsOn;
}
