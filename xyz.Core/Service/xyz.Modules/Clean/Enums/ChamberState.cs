namespace xyz.Modules.Enums;

/// <summary>
/// 腔体状态码；公共搬运状态继承自 <see cref="TransferModuleState"/>
/// </summary>
public class ChamberState : TransferModuleState
{
    /// <summary>
    /// 回零中
    /// </summary>
    public const int Homing = 100;

    /// <summary>
    /// 工艺中
    /// </summary>
    public const int Processing = 110;

    /// <summary>
    /// 手动页的设备动作中（轴、气缸）
    /// </summary>
    public const int Manual = 120;
}

/// <summary>
/// 腔体的动作（状态表、动作入口按它查）
/// </summary>
public enum ChamberAction
{
    Home,
    Process,
    Reset,
    Abort,

    /// <summary>
    /// 手动页的设备动作（轴回零 / 移动 / 步进 / 点动 / 复位，气缸升 / 降）
    /// </summary>
    Manual,

    /// <summary>
    /// 取放准备二：卡盘转到取放位、开腔门（搬运管理在机械手伸手之前发，手动页的取放也经它）
    /// </summary>
    PrepareTransfer2,
}
