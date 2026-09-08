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
}

public enum LoadPortAction
{
    Load,
    Unload,
    Home,
    Reset,
    Abort,
    Online,
    Offline
}

