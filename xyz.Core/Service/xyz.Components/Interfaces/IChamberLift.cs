namespace xyz.Components.Interfaces;

/// <summary>
/// axis 的升降组件接口
/// </summary>
public interface IChamberLift : ICylinder
{
    bool Up();

    bool Down();
}

