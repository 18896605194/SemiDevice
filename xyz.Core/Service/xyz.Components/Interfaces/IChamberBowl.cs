namespace xyz.Components.Interfaces;

/// <summary>腔体接液杯：可以装多层，每层独立提供升降和到位反馈。</summary>
public interface IChamberBowl : ICylinder
{
    /// <summary>升起接液杯。</summary>
    bool Up();

    /// <summary>降下接液杯。</summary>
    bool Down();
}

