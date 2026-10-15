using xyz.Components.Attributes;
using xyz.Components.Interfaces;

namespace xyz.Components.Components;

[Component(description: "气缸驱动的接液杯，节点名不参与接液杯识别。")]
public class ChamberBowlComponent : CylinderComponent, IChamberBowl
{
    public bool Up()
    {
        return Open();
    }

    public bool Down()
    {
        return Close();
    }
}

