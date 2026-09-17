namespace xyz.Drivers.Loadport.FCD.Commands;

/// <summary>
/// Load（MOV:CLOAD）：开门并 Mapping。
/// Mapping 数据两种上报形状都收：先推独立的 INF:MAPDT/&lt;槽位串&gt; 帧、再推 INF:CLOAD 终结；
/// 或直接挂在终结帧上（INF:CLOAD/&lt;槽位串&gt;）。终结时归一化进 Response.SlotMap。
/// </summary>
public sealed class FcdLoadCommand : FcdCommand
{
    private const string MapDataPrefix = "INF:MAPDT/";

    private string _mapData = string.Empty;

    public FcdLoadCommand(ILoadPortDriver driver) : base(driver)
    {
    }

    protected override string Name => "CLOAD";

    public override string BuildMsg()
    {
        return $"{FcdProtocol.Move}:CLOAD";
    }

    public override bool ParseMsg(string body)
    {
        // INF:MAPDT/<槽位串> 是 Load 过程的附带数据帧：认领暂存，不构成终态。
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
