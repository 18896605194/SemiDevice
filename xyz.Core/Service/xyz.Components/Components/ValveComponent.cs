using xyz.Components.Attributes;

namespace xyz.Components.Components;

/// <summary>
/// 阀门组件：单 DO 驱动、可选到位反馈——IO 点位与到位判定都在 <see cref="OneStateComponent"/>。
/// 通电开、断电靠弹簧回关位，关那一侧不受控。
/// </summary>
[Component(description: "阀门组件 (单 DO 驱动，可选到位反馈)")]
public class ValveComponent : OneStateComponent
{
}
