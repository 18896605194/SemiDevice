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
/// 对准器的动作名（状态表、动作入口按它查）；公共动作继承自 <see cref="ModuleAction"/>
/// </summary>
public class AlignerAction : ModuleAction
{
    public const string Align = "Align";
}
