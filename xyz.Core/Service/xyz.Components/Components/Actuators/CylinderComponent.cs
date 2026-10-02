using xyz.Components.Attributes;

namespace xyz.Components.Components;

/// <summary>
/// 气缸组件：双 DO 驱动、两侧到位反馈各自可选——IO 点位与到位判定都在 <see cref="TwoStateComponent"/>。
/// 伸缩两个方向都受控，断电保持在原位。
/// 单作用气缸（一个 DO、靠自重/弹簧回位）不用这个，从 <see cref="OneStateComponent"/> 派生。
/// </summary>
[Component(description: "气缸组件 (双 DO 驱动，两侧到位反馈各自可选)")]
public class CylinderComponent : TwoStateComponent
{
}
