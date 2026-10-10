namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// Map（MOV:CLDMP）：原地扫一遍槽，不装载——XM.Core 实机 Fortrend 驱动、FCD 仿真器都是这么用的。
/// FCD 没有带图卸载的指令，自动跑货要对账时 Unload 关好门再发它；槽位串怎么收见 <see cref="FcdMappingCommand"/>。
/// </summary>
public sealed class FcdMapCommand : FcdMappingCommand
{
    public FcdMapCommand(ILoadPortDriver driver) : base(driver)
    {
    }

    protected override string Name => "CLDMP";

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Move}:CLDMP";
    }
}
