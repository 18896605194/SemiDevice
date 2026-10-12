using xyz.Components.Attributes;
using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>气缸驱动的腔门，复用气缸的接线、反馈和超时判断。</summary>
[Component(description: "气缸驱动的腔门，复用气缸的接线、反馈和超时判断。")]
public class ChamberDoorComponent : CylinderComponent, IChamberDoor
{
}

