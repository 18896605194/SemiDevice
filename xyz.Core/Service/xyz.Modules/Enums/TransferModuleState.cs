namespace xyz.Modules.Enums;

/// <summary>
/// 可参与晶圆搬运模块的公共状态码。
/// </summary>
public class TransferModuleState : ModuleState
{
    public const int PreTransfer = 50;

    public const int TransferReady = 60;

    public const int Transferring = 70;

    public const int TransferComplete = 80;
}
