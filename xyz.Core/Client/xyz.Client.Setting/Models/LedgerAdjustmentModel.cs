using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;

namespace xyz.Client.Setting.Models;

/// <summary>
/// 调整记录表的一行：一次人工移账或删账。
/// </summary>
public sealed class LedgerAdjustmentModel
{
    /// <param name="dto">后端给的记录。</param>
    /// <param name="positionOf">位置 + 槽号 → "Robot1 · 手指 2"（机械手、别的位置槽名不一样，按当前的位置表翻）。</param>
    /// <param name="isFresh">是不是本机刚做完的那一条（淡强调底）。</param>
    public LedgerAdjustmentModel(WaferAdjustmentDto dto, Func<string, int, string> positionOf, bool isFresh)
    {
        Time = dto.Time;
        Action = dto.Action;
        WaferId = dto.WaferId;
        Operator = dto.Operator;
        Reason = string.IsNullOrWhiteSpace(dto.Reason) ? L10n.Get("setting.ledger.no_reason") : dto.Reason;
        IsFresh = isFresh;

        string from = positionOf(dto.FromModule, dto.FromSlot);
        PathText = dto.ToModule is null || dto.ToSlot is null
            ? from
            : $"{from} → {positionOf(dto.ToModule, dto.ToSlot.Value)}";
    }

    public DateTime Time { get; }

    /// <summary>
    /// Move（移动）/ Delete（删除），表格按它出字和颜色。
    /// </summary>
    public string Action { get; }

    public string WaferId { get; }

    /// <summary>
    /// 从哪到哪；删账只有"从哪"。
    /// </summary>
    public string PathText { get; }

    public string Operator { get; }

    public string Reason { get; }

    public bool IsFresh { get; }
}
