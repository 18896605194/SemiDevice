using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;

namespace xyz.Client.Setting.Models;

/// <summary>
/// 账单调整页位置列表里的一项：一个能放片的位置（机械手、LoadPort、腔体，或 Aligner、Buffer 这类）和它的槽。
/// 每次重拉账整个重建、不就地改；源、目标两栏的列表用的是同一批对象——源栏看有几片，目标栏看空几个槽。
/// </summary>
public sealed class LedgerLocationModel
{
    public LedgerLocationModel(WaferLocationDto dto)
    {
        Name = dto.Name;
        Kind = dto.Kind;
        Slots = dto.Slots.OrderBy(slot => slot.Slot).ToList();
        SlotCount = Slots.Count;
        WaferCount = Slots.Count(slot => slot.Wafer is not null);

        KindText = L10n.Get(Kind switch
        {
            WaferLocationKind.Robot => "setting.ledger.kind.robot",
            WaferLocationKind.LoadPort => "setting.ledger.kind.loadport",
            WaferLocationKind.Chamber => "setting.ledger.kind.chamber",
            _ => "setting.ledger.kind.other",
        });

        string summary = L10n.Get(IsRobot ? "setting.ledger.summary_arms" : "setting.ledger.summary_slots", SlotCount, WaferCount);
        if (Kind == WaferLocationKind.LoadPort)
        {
            summary += " · " + (string.IsNullOrEmpty(dto.CarrierId) ? L10n.Get("setting.ledger.no_carrier") : dto.CarrierId);
        }

        SummaryText = summary;
    }

    /// <summary>
    /// 位置名（模块名，如 Robot1、LoadPort2、Chamber5），移账、删账都按它。
    /// </summary>
    public string Name { get; }

    public WaferLocationKind Kind { get; }

    /// <summary>
    /// 机械手的槽就是手指，显示"手指 1"；其他位置显示"槽 01"。
    /// </summary>
    public bool IsRobot => Kind == WaferLocationKind.Robot;

    /// <summary>
    /// 种类名，位置列表按它分组（机械手、LoadPort、腔体、其他）。
    /// </summary>
    public string KindText { get; }

    /// <summary>
    /// 槽位表标题条上的一句：几槽（几只手指）几片，LoadPort 再带载具号。
    /// </summary>
    public string SummaryText { get; }

    /// <summary>
    /// 每个槽一项，按槽号从小到大。
    /// </summary>
    public IReadOnlyList<WaferSlotDto> Slots { get; }

    public int SlotCount { get; }

    public int WaferCount { get; }

    public int FreeCount => SlotCount - WaferCount;

    /// <summary>
    /// 目标栏位置后面的"空位 N"（英文数字在前，所以整句走语言包模板）。
    /// </summary>
    public string FreeText => L10n.Get("setting.ledger.free_count", FreeCount);

    /// <summary>
    /// 有片：源栏里能从这儿选片。
    /// </summary>
    public bool HasWafer => WaferCount > 0;

    /// <summary>
    /// 有空槽：目标栏里能往这儿放。
    /// </summary>
    public bool HasFree => FreeCount > 0;

    /// <summary>
    /// 槽名："槽 01"，机械手是"手指 1"。
    /// </summary>
    public string SlotName(int slot)
    {
        return L10n.Get(IsRobot ? "setting.ledger.arm_name" : "setting.ledger.slot_name", slot);
    }

    /// <summary>
    /// 位置 + 槽名："Chamber5 · 槽 01"。
    /// </summary>
    public string PositionText(int slot)
    {
        return $"{Name} · {SlotName(slot)}";
    }
}
