namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// Load（MOV:CLOAD）：开门并 Mapping，槽位串怎么收见 <see cref="FcdMappingCommand"/>。
/// </summary>
public sealed class FcdLoadCommand : FcdMappingCommand
{
    public FcdLoadCommand(ILoadPortDriver driver) : base(driver)
    {
    }

    protected override string Name => "CLOAD";

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Move}:CLOAD";
    }
}
