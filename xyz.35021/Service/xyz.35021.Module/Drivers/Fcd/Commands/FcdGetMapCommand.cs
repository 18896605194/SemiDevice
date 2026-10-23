using xyz.Drivers.Loadport;

namespace xyz._35021.Module.Drivers.Fcd.Commands;

/// <summary>
/// 取槽图（GET:MAPDT）：设备回最后一次扫片的结果（正序，第 1 个字符是第 1 槽），槽位串随 ACK 回、ACK 就收尾
/// </summary>
public sealed class FcdGetMapCommand : FcdCommand
{
    protected override string Name => "MAPDT";

    /// <summary>
    /// 取到的槽图（下标 0 是第 1 槽）
    /// </summary>
    public IReadOnlyList<SlotState> SlotMap { get; private set; } = [];

    /// <summary>
    /// 设备回的槽位串原文（E / P / C / D 那串），跟槽图一起交给平台写日志
    /// </summary>
    public string Raw { get; private set; } = string.Empty;

    public override string BuildFrame()
    {
        return $"{FcdProtocol.Get}:MAPDT";
    }

    protected override bool Handle(FcdMessage message)
    {
        if (!string.Equals(message.Name, Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        switch (message.Type)
        {
            case FcdProtocol.Ack:
            case FcdProtocol.Inf:
                // 查询类：槽位串随 ACK 回，后面没有 INF；万一回的是 INF 也一样收
                Raw = message.Data;
                SlotMap = ParseSlotMap(message.Data);
                Complete();
                return true;

            case FcdProtocol.Abs:
            case FcdProtocol.Nak:
                Fail($"{message.Type}:{Name}/{message.Data}");
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// 富创得的槽位串换成平台的槽位状态（E 空、P 有片、C 交叉、D 叠片），下标 0 是第 1 槽；认不出的字符记 Undefined
    /// </summary>
    private static IReadOnlyList<SlotState> ParseSlotMap(string mapData)
    {
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
}
