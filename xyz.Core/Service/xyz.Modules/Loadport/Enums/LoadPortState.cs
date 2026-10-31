namespace xyz.Modules.Enums;

/// <summary>
/// LoadPort 状态码；公共搬运状态继承自 <see cref="TransferModuleState"/>。
/// </summary>
public class LoadPortState : TransferModuleState
{
    public const int Loading = 100;

    public const int Loaded = 110;

    public const int Unloading = 120;

    public const int Homing = 130;

    public const int Clamping = 140;

    public const int Unclamping = 150;

    public const int Mapping = 160;
}

/// <summary>
/// LoadPort 的动作名（状态表、动作入口按它查）；公共动作继承自 <see cref="ModuleAction"/>
/// </summary>
public class LoadPortAction : ModuleAction
{
    public const string Load = "Load";

    public const string Unload = "Unload";

    public const string Clamp = "Clamp";

    public const string Unclamp = "Unclamp";

    public const string Map = "Map";
}

