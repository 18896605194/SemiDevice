using xyz.Components.Attributes;
using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>气缸驱动的接液杯，节点名不参与接液杯识别。</summary>
[Component(description: "气缸驱动的接液杯，节点名不参与接液杯识别。")]
public class ChamberBowlComponent : CylinderComponent, IChamberBowl
{
    /// <summary>升起接液杯。</summary>
    public bool Up()
    {
        return Open();
    }

    /// <summary>降下接液杯。</summary>
    public bool Down()
    {
        return Close();
    }
}

