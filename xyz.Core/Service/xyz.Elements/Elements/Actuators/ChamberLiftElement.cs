using xyz.Elements.Attributes;
using xyz.Elements.Interfaces;

namespace xyz.Elements.Elements;

/// <summary>摆臂的气缸升降元件，与摆臂下其他气缸按接口区分。</summary>
[Element(description: "摆臂的气缸升降元件，与摆臂下其他气缸按接口区分。")]
public class ChamberLiftElement : CylinderElement, IChamberLift
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

