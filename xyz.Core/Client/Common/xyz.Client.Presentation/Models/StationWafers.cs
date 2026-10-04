using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 站点上的片 → 圆片模型：LoadPort 花篮、腔体片位都以晶圆账为准，颜色口径跟账单调整页一样（WaferStates）。
/// LoadPort 手动页、腔体手动页、调度图的站点卡片（Robot 手动页、主界面）都用这一份。
/// </summary>
public static class StationWafers
{
    /// <summary>
    /// 花篮里的片：有片的槽各给一片，交给 LoadPort 控件按槽位号摆。以晶圆账为准（取放片、人工改账都跟着变，颜色按片的状态）；
    /// 账上没登记这个 LoadPort 时才退回按 Mapping 结果画（叠片 / 交叉片用各自的状态色）。
    /// </summary>
    public static IReadOnlyList<WaferModel> OfLoadPort(IReadOnlyList<WaferSlotDto> ledgerSlots, IReadOnlyList<LoadPortSlotDto> mapping)
    {
        if (ledgerSlots.Count > 0)
        {
            var wafers = new List<WaferModel>();
            foreach (var slot in ledgerSlots)
            {
                var wafer = slot.Wafer;
                if (wafer is not null)
                {
                    wafers.Add(new WaferModel { Slot = slot.Slot, LpSlot = slot.Slot.ToString("00"), State = WaferStates.Of(wafer) });
                }
            }

            return wafers;
        }

        return mapping
            .Where(slot => slot.HasWafer)
            .Select(slot => new WaferModel
            {
                Slot = slot.Slot,
                LpSlot = slot.Slot.ToString("00"),
                State = slot.State switch
                {
                    LoadPortSlotState.DoubleSlotted => "Double",
                    LoadPortSlotState.CrossSlotted => "Crossed",
                    _ => "IdleHasjob",
                },
            })
            .ToList();
    }

    /// <summary>
    /// 腔体片位上的片：颜色沿用圆片控件的状态色——未做 = 待加工、工艺中、做完、失败 / 中止 = 报错；片上写它从哪个 LoadPort 的第几槽来。
    /// </summary>
    public static WaferModel OfChamber(ChamberSlotDto slot)
    {
        string state;
        switch (slot.State)
        {
            case ChamberSlotState.InProcess:
                state = "Process";
                break;
            case ChamberSlotState.Completed:
                state = "Completed";
                break;
            case ChamberSlotState.Failed:
            case ChamberSlotState.Aborted:
                state = "Error";
                break;
            default:
                state = "IdleHasjob";
                break;
        }

        return new WaferModel
        {
            Slot = slot.Slot,
            LpSlot = WaferLabel.Of(slot.SourceLoadPort, slot.SourceSlot),
            State = state,
        };
    }
}
