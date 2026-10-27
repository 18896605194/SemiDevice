using xyz.Elements.Attributes;

using xyz.Elements.Interfaces;

namespace xyz.Elements.Elements;


[Element(description: "气缸元件 (双 DO 驱动，两侧到位反馈挂子节点 Opened / Closed，各自可选)")]
public class CylinderElement : TwoStateElement, ICylinder
{
}
