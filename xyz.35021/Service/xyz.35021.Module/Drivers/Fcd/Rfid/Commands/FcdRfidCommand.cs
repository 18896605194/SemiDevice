using xyz.Drivers.Device;

namespace xyz._35021.Module.Drivers.Fcd.Rfid.Commands;

/// <summary>
/// 富创得读头的一条指令：自带下发块；读头握手是单事务，同一时刻只有一条在途（都用同一个在途键）；回复怎么认每条指令在自己的 Handle 里写
/// </summary>
public abstract class FcdRfidCommand : DeviceCommand<FcdRfidMessage>
{
    /// <summary>
    /// 在途键：读头一次只认一条，所有指令共用
    /// </summary>
    public override string Key => "FcdRfid";

    /// <summary>
    /// 下发的块（MSG_ID + DATA），不带 LEN / 校验——那些由解码器包
    /// </summary>
    protected abstract byte[] BuildBlock();

    /// <summary>
    /// 帧体：块的字节按 Latin1 过字符串
    /// </summary>
    public override string BuildFrame()
    {
        return FcdRfidProtocol.Binary.GetString(BuildBlock());
    }
}
