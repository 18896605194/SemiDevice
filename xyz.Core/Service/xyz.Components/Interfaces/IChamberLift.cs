namespace xyz.Components.Interfaces;

/// <summary>装在摆臂上的升降部件，由所属摆臂按接口找到。</summary>
public interface IChamberLift : ICylinder
{
    /// <summary>升起摆臂。</summary>
    bool Up();

    /// <summary>降下摆臂。</summary>
    bool Down();
}

