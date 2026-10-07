using xyz.Components.Components;
using xyz.Components.Models;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 把晶圆账上一个位置的槽转成推给界面的样子（WaferSlotDto）。机械手、LoadPort 的状态推送和账单调整页共用这一份，
/// 各页面画片的口径就一样：都以账为准。
/// </summary>
public static class WaferLedgerSnapshot
{
    /// <summary>
    /// 这个位置每个槽的片（槽号从 1 起，空槽的 Wafer 为 null）；晶圆账没开或没登记这个位置时为空表。
    /// 模块每拍扫描都调，账一变下一拍的状态推送就跟着变。
    /// </summary>
    public static List<WaferSlotDto> SlotsOf(string module)
    {
        var ledger = WaferManagerComponent.Current;
        if (ledger is null || !ledger.IsEnable)
        {
            return [];
        }

        return ToSlots(ledger.GetSlots(module));
    }

    public static List<WaferSlotDto> ToSlots(IReadOnlyList<WaferInfo?> slots)
    {
        var result = new List<WaferSlotDto>(slots.Count);
        for (int index = 0; index < slots.Count; index++)
        {
            var wafer = slots[index];
            result.Add(new WaferSlotDto
            {
                Slot = index + 1,
                Wafer = wafer is null ? null : ToDto(wafer),
            });
        }

        return result;
    }

    public static WaferDto ToDto(WaferInfo wafer)
    {
        return new WaferDto
        {
            WaferId = wafer.WaferId,
            LotId = wafer.LotId,
            CarrierId = wafer.CarrierId,
            Status = wafer.Status.ToString(),
            ProcessState = wafer.ProcessState.ToString(),
            SourceLoadPort = wafer.SourceLoadPort,
            SourceSlot = wafer.SourceSlot,
        };
    }
}
