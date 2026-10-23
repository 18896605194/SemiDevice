using xyz.Drivers.Loadport;

namespace xyz._35021.Module.Drivers.Fcd;

/// <summary>
/// 富创得 LoadPort（LP300）B 类帧协议：指令类型前缀、回复类型、Mapping 字符
/// </summary>
public static class FcdProtocol
{
    #region 指令类型前缀（3 字符）

    /// <summary>
    /// 查询类指令
    /// </summary>
    public const string Get = "GET";

    /// <summary>
    /// 动作类指令
    /// </summary>
    public const string Move = "MOV";

    /// <summary>
    /// 设置类指令
    /// </summary>
    public const string Set = "SET";

    #endregion

    #region 回复类型

    /// <summary>
    /// 受理（查询类的数据随它回、它就是收尾）
    /// </summary>
    public const string Ack = "ACK";

    /// <summary>
    /// 动作做完 / 主动事件
    /// </summary>
    public const string Inf = "INF";

    /// <summary>
    /// 异常结束
    /// </summary>
    public const string Abs = "ABS";

    /// <summary>
    /// 拒绝
    /// </summary>
    public const string Nak = "NAK";

    #endregion

    #region Mapping 字符（E=空 P=有片 C=交叉 D=双片 ?=认不出）

    /// <summary>
    /// 把 FCD 的 Mapping 槽位串换成我们的槽位状态，下标 0 是第 1 槽；认不出的字符记 Undefined
    /// </summary>
    internal static IReadOnlyList<SlotState> ParseSlotMap(string mapData)
    {
        ArgumentNullException.ThrowIfNull(mapData);

        string trimmed = mapData.Trim();
        var slots = new SlotState[trimmed.Length];
        for (int i = 0; i < trimmed.Length; i++)
        {
            switch (char.ToUpperInvariant(trimmed[i]))
            {
                case 'E':
                    slots[i] = SlotState.Empty;
                    break;
                case 'P':
                    slots[i] = SlotState.Normal;
                    break;
                case 'C':
                    slots[i] = SlotState.Crossed;
                    break;
                case 'D':
                    slots[i] = SlotState.Double;
                    break;
                default:
                    slots[i] = SlotState.Undefined;
                    break;
            }
        }

        return slots;
    }

    #endregion
}
