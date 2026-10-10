namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// 带 Mapping 的动作指令（Load、Map）：槽位串两种上报形状都收——先推独立的 INF:MAPDT/&lt;槽位串&gt; 帧、再推终结帧；
/// 或直接挂在终结帧上（INF:&lt;指令名&gt;/&lt;槽位串&gt;）。终结时归一化进 Response.SlotMap，两种都没有就是空槽图（上层按槽数对不上处理）。
/// </summary>
public abstract class FcdMappingCommand : FcdCommand
{
    private const string MapDataPrefix = "INF:MAPDT/";
    private string _mapData = string.Empty;

    protected FcdMappingCommand(ILoadPortDriver driver) : base(driver)
    {
    }

    public override bool ParseMsg(string body)
    {
        // INF:MAPDT/<槽位串> 是动作过程中的附带数据帧：认领暂存，不构成终态。
        if (body.StartsWith(MapDataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            _mapData = body[MapDataPrefix.Length..];
            return true;
        }

        return base.ParseMsg(body);
    }

    protected override LoadPortResponse BuildResponse(string data)
    {
        // 终结帧自带槽位串优先，否则用之前独立 MAPDT 帧暂存的。
        string mapData = data.Length > 0 ? data : _mapData;
        return new LoadPortResponse
        {
            IsSuccess = true,
            Content = mapData,
            SlotMap = FcdProtocol.ParseSlotMap(mapData),
        };
    }
}
