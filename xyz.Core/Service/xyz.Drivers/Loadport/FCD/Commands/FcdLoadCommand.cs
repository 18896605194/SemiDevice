namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// Load（MOV:CLOAD）：开门并 Mapping。
/// 完成前设备先推 INF:MAPDT 槽位数据帧（本指令认领存入 SlotMap），INF:CLOAD 终结。
/// </summary>
public sealed class FcdLoadCommand : FcdCommand
{
    private const string MapDataPrefix = "INF:MAPDT/";

    public FcdLoadCommand(LoadPortDriverBase driver) : base(driver)
    {
    }

    protected override string Name => "CLOAD";

    /// <summary>
    /// Mapping 槽位数据（随 INF:MAPDT 推送，先于完成），如 25 个 P。
    /// </summary>
    public string SlotMap { get; private set; } = string.Empty;

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Move}:CLOAD";
    }

    public override bool ParseMsg(string body)
    {
        // INF:MAPDT/<槽位串> 是 Load 过程的附带数据帧：认领存数据，不构成终态。
        if (body.StartsWith(MapDataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            SlotMap = body[MapDataPrefix.Length..];
            return true;
        }

        return base.ParseMsg(body);
    }
}
