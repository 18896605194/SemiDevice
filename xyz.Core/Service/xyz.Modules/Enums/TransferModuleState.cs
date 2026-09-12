namespace xyz.Modules.Enums;

/// <summary>
/// 可参与晶圆搬运模块的公共状态码。
/// 机械手交互标准环（所有被机械手服务的模块统一遵循）：
/// 锚点态（模块就绪可服务，如 LoadPort 的 Loaded）→ TransferReady → Transferring
/// → TransferComplete → 回到锚点态；机械手每轮取放片走一遍环，回到锚点态后允许下一轮或后续动作。
/// </summary>
public class TransferModuleState : ModuleState
{
    public const int PreTransfer = 50;

    public const int TransferReady = 60;

    public const int Transferring = 70;

    public const int TransferComplete = 80;
}
