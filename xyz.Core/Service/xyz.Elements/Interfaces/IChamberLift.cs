namespace xyz.Elements.Interfaces;

/// <summary>
/// axis 的升降元件接口
/// </summary>
public interface IChamberLift : ICylinder
{
    bool Up();

    bool Down();
}

