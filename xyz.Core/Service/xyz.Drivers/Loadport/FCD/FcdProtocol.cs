namespace xyz.Drivers.Loadport.FCD;

/// <summary>
/// FCD B 类帧协议常量与通用解析：指令类型前缀、回复类型、Mapping 字符。
/// </summary>
public static class FcdProtocol
{
    #region 指令类型前缀（3 字符）

    /// <summary>查询类指令。</summary>
    public const string Get = "GET";

    /// <summary>动作类指令。</summary>
    public const string Move = "MOV";

    /// <summary>设置类指令。</summary>
    public const string Set = "SET";

    #endregion

    #region 回复类型

    /// <summary>受理。</summary>
    public const string Ack = "ACK";

    /// <summary>完成通知 / 主动事件。</summary>
    public const string Inf = "INF";

    /// <summary>异常完成。</summary>
    public const string Abs = "ABS";

    /// <summary>拒绝。</summary>
    public const string Nak = "NAK";

    #endregion

    #region Mapping 字符（E=空 P=有片 C=交叉 D=双片 ?=无法识别）

    /// <summary>
    /// 把 FCD Mapping 槽位串归一化为厂商无关的槽位状态，下标 0 对应第 1 槽；无法识别的字符记为 Undefined。
    /// 只在 FCD 指令内部使用，结果经 LoadPortResponse.SlotMap 交给上层。
    /// </summary>
    internal static IReadOnlyList<SlotState> ParseSlotMap(string mapData)
    {
        ArgumentNullException.ThrowIfNull(mapData);

        string trimmed = mapData.Trim();
        var slots = new SlotState[trimmed.Length];
        for (int i = 0; i < trimmed.Length; i++)
        {
            slots[i] = char.ToUpperInvariant(trimmed[i]) switch
            {
                'E' => SlotState.Empty,
                'P' => SlotState.CorrectlyOccupied,
                'C' => SlotState.CrossSlotted,
                'D' => SlotState.DoubleSlotted,
                _ => SlotState.Undefined,
            };
        }

        return slots;
    }

    #endregion
}
