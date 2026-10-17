using xyz.Components.Attributes;

using xyz.Components.Interfaces;

namespace xyz.Components.Components;


[Component(description: "气缸组件 (双 DO 驱动，两侧到位反馈挂子节点 Opened / Closed，各自可选)")]
public class CylinderComponent : TwoStateComponent, ICylinder
{
}
