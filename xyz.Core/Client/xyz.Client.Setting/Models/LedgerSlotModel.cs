using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;

namespace xyz.Client.Setting.Models;

/// <summary>
/// 槽位表的一行：选中位置的一个槽和槽上的片。源栏只能点有片的槽、目标栏只能点空槽（IsPickable）。
/// 选好的源 / 目标也用它记（位置、槽号、片号），重拉账后按位置名 + 槽号找回来。
/// </summary>
public sealed class LedgerSlotModel
{
    public LedgerSlotModel(LedgerLocationModel location, WaferSlotDto slot, bool forSource)
    {
        Location = location;
        Slot = slot.Slot;
        Wafer = slot.Wafer;
        SlotText = location.SlotName(slot.Slot);
        PositionText = location.PositionText(slot.Slot);
        IsPickable = forSource ? Wafer is not null : Wafer is null;

        var wafer = slot.Wafer;
        if (wafer is not null)
        {
            StateText = StateTextOf(wafer);
            Tone = ToneOf(wafer);
        }
    }

    public LedgerLocationModel Location { get; }

    public int Slot { get; }

    /// <summary>
    /// 槽上的片；空槽为 null。
    /// </summary>
    public WaferDto? Wafer { get; }

    public string WaferId => Wafer?.WaferId ?? string.Empty;

    public string LotId => Wafer?.LotId ?? string.Empty;

    /// <summary>
    /// "槽 01"，机械手是"手指 1"。
    /// </summary>
    public string SlotText { get; }

    /// <summary>
    /// "Chamber5 · 槽 01"。
    /// </summary>
    public string PositionText { get; }

    /// <summary>
    /// 片的状态："已完成"，不是正常片时带上物理状态（"交叉片 · 未处理"）；空槽为空。
    /// </summary>
    public string StateText { get; } = string.Empty;

    /// <summary>
    /// 色条的色调（WaferBarStyle 的 Tag）：Idle / InProcess / Completed / Error / Crossed / Double / Dummy / Unknown，空槽 Empty。
    /// </summary>
    public string Tone { get; } = "Empty";

    /// <summary>
    /// 这一栏里能不能点它：源栏要有片，目标栏要是空槽。
    /// </summary>
    public bool IsPickable { get; }

    /// <summary>
    /// 跟另一个是不是同一个槽（位置名不分大小写）。
    /// </summary>
    public bool IsSameSlot(LedgerSlotModel? other)
    {
        return other is not null
            && other.Slot == Slot
            && string.Equals(other.Location.Name, Location.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static string StateTextOf(WaferDto wafer)
    {
        string process = L10n.Get("setting.ledger.process." + wafer.ProcessState);
        if (string.IsNullOrEmpty(wafer.Status) || wafer.Status == "Normal")
        {
            return process;
        }

        return L10n.Get("setting.ledger.status_with_process", L10n.Get("setting.ledger.status." + wafer.Status), process);
    }

    /// <summary>
    /// 物理状态不正常的（交叉、叠片、陪片、不明）按物理状态上色，正常片按工艺状态上色。
    /// </summary>
    private static string ToneOf(WaferDto wafer)
    {
        switch (wafer.Status)
        {
            case "Crossed":
            case "Double":
            case "Dummy":
            case "Unknown":
                return wafer.Status;
        }

        switch (wafer.ProcessState)
        {
            case "InProcess":
            case "Completed":
                return wafer.ProcessState;
            case "Failed":
            case "Aborted":
                return "Error";
            default:
                return "Idle";
        }
    }
}
