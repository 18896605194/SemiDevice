using xyz.Elements.Attributes;
using xyz.Elements.Interfaces;

namespace xyz.Elements.Elements;

[Element(description: "气缸驱动的接液杯，节点名不参与接液杯识别。")]
public class ChamberBowlElement : CylinderElement, IChamberBowl
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

