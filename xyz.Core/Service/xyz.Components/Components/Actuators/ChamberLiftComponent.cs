using xyz.Components.Attributes;
using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>摆臂的气缸升降组件，与摆臂下其他气缸按接口区分。</summary>
[Component(description: "摆臂的气缸升降组件，与摆臂下其他气缸按接口区分。")]
public class ChamberLiftComponent : CylinderComponent, IChamberLift
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

