namespace xyz.Modules.Enums;

/// <summary>
/// 对准器状态码；公共搬运状态继承自 <see cref="TransferModuleState"/>
/// </summary>
public class AlignerState : TransferModuleState
{
    /// <summary>
    /// 回零中（台上有片的，回零后接着对一遍也算在里面）
    /// </summary>
    public const int Homing = 100;

    /// <summary>
    /// 对准中
    /// </summary>
    public const int Aligning = 110;
}

/// <summary>
/// 对准器的动作（状态表、动作入口按它查）
/// </summary>
public enum AlignerAction
{
    Home,

    /// <summary>
    /// 对准：片放上来以后模块自己起
    /// </summary>
    Align,

    Reset,
    Abort,
}
