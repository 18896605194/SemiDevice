namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// 带 Mapping 的 Unload（MOV:CUDMP）：关门时再扫一遍槽，槽位串怎么收见 <see cref="FcdMappingCommand"/>。
/// 指令名照 TDK 系协议（老 CTC 的 Hirata-II 驱动用的就是 CUDMP），FCD 手册到手后要核对。
/// </summary>
public sealed class FcdUnloadWithMapCommand : FcdMappingCommand
{
    public FcdUnloadWithMapCommand(ILoadPortDriver driver) : base(driver)
    {
    }

    protected override string Name => "CUDMP";

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Move}:CUDMP";
    }
}
