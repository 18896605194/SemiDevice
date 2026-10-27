namespace xyz.Modules.Enums;

/// <summary>
/// 对准器平台
/// </summary>
public class AlignerState : TransferModuleState
{
    public const int Homing = 100;

    public const int Aligning = 110;
}

/// <summary>
/// 对准器的动作
/// </summary>
public enum AlignerAction
{
    Home,
    Align,
    Reset,
    Abort,
}
