namespace xyz.Modules.Enums;


public class ChamberState : TransferModuleState
{
    public const int Homing = 100;

    /// <summary>工艺中。</summary>
    public const int Processing = 110;
}

public enum ChamberAction
{
    Home,
    Process,
    Reset,
    Abort,
}
