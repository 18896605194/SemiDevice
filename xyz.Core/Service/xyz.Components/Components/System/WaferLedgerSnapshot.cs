using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Shared.Dtos;

namespace xyz.Components.Components;

/// <summary>
/// 把晶圆账上一个位置的槽转成推给界面的样子（WaferSlotDto）。机械手、LoadPort 的状态推送和账单调整页共用这一份，
/// 各页面画片的口径就一样：都以账为准。
/// </summary>
public static class WaferLedgerSnapshot
{
    /// <summary>
    /// 这个位置每个槽的片（槽号从 1 起，空槽的 Wafer 为 null）；晶圆账没开、没登记这个位置、映射组件没装时为空表。
    /// 模块每拍扫描都调（传它自己取到的晶圆账、映射组件），账一变下一拍的状态推送就跟着变。
    /// </summary>
    public static List<WaferSlotDto> SlotsOf(ILedgerManager? ledger, IMapper? mapper, string module)
    {
        if (ledger is null || !ledger.IsEnable || mapper is null)
        {
            return [];
        }

        return ToSlots(mapper, ledger.GetSlots(module));
    }

    /// <summary>
    /// 一排槽转成给界面的样子：片走映射组件转（同名字段照抄，状态转成文字）
    /// </summary>
    public static List<WaferSlotDto> ToSlots(IMapper mapper, IReadOnlyList<WaferInfo?> slots)
    {
        var result = new List<WaferSlotDto>(slots.Count);
        for (int index = 0; index < slots.Count; index++)
        {
            var slot = new WaferSlotDto { Slot = index + 1 };
            var wafer = slots[index];
            if (wafer is not null)
            {
                slot.Wafer = mapper.Map<WaferDto>(wafer);
            }

            result.Add(slot);
        }

        return result;
    }
}
