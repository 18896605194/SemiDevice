using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;

namespace xyz.Client.Main.Models;

/// <summary>
/// 主界面右栏的一个 LoadPort 页签：载具、槽图认定到哪一步、槽数（LoadPort 状态推送），LotID、Sequence（人填的），
/// 槽位表（大号在上，片以晶圆账为准，账上没登记这个 LoadPort 时才按 Mapping 结果）。
/// 推送来了就地刷新：行的实例保留，人选过的 Sequence 不会被冲掉。
/// </summary>
public sealed class LoadPortJobModel : ObservableObject
{
    public LoadPortJobModel(string name)
    {
        Name = name;
    }

    /// <summary>LoadPort 模块名（sc.xml 原样，如 LoadPort1），也是页签上的字和状态推送的 token。</summary>
    public string Name { get; }

    private string _carrierId = string.Empty;

    /// <summary>载具号（RFID 读到的）；没读到为空。</summary>
    public string CarrierId
    {
        get => _carrierId;
        private set
        {
            if (SetProperty(ref _carrierId, value))
            {
                OnPropertyChanged(nameof(CarrierText));
            }
        }
    }

    /// <summary>载具号显示：没读到写"—"。</summary>
    public string CarrierText => string.IsNullOrEmpty(CarrierId) ? L10n.Get("main.none") : CarrierId;

    private CarrierSlotMapStatus _mapStatus;

    /// <summary>槽图认定到哪一步（界面按它写"映射完成"这类字、换色）。</summary>
    public CarrierSlotMapStatus MapStatus
    {
        get => _mapStatus;
        private set => SetProperty(ref _mapStatus, value);
    }

    private int _slotCount;

    /// <summary>花篮槽数（sc.xml LoadPort 的 SlotCount）；还没收到推送为 0，槽位表是空的。</summary>
    public int SlotCount
    {
        get => _slotCount;
        private set => SetProperty(ref _slotCount, value);
    }

    private string _lotId = string.Empty;

    /// <summary>批次号（人填的，建 Job 时带上；Job 还没做）。</summary>
    public string LotId
    {
        get => _lotId;
        set => SetProperty(ref _lotId, value);
    }

    private string _sequence = string.Empty;

    /// <summary>
    /// 整篮的 Sequence（流程配方名）：一选就给所有能做的片都套上（单独改用 ⊕，不做的用 ⊖）；
    /// 之后才放上、才 Mapping 出来的片也套它。
    /// </summary>
    public string Sequence
    {
        get => _sequence;
        set
        {
            if (!SetProperty(ref _sequence, value))
            {
                return;
            }

            foreach (var slot in Slots)
            {
                if (slot.CanAssign)
                {
                    slot.Sequence = value;
                }
            }
        }
    }

    /// <summary>槽位表，大号在上（25 → 01），跟花篮、账单调整页一样。</summary>
    public ObservableCollection<JobSlotModel> Slots { get; } = [];

    /// <summary>
    /// 用 LoadPort 的状态推送刷新（界面线程调用）。槽数变了才重建行，其他时候就地改，人选过的 Sequence 留着。
    /// </summary>
    public void Update(LoadPortDto dto)
    {
        CarrierId = dto.CarrierId ?? string.Empty;
        MapStatus = dto.CarrierSlotMapStatus;
        SlotCount = Math.Max(dto.SlotCount, 0);

        if (Slots.Count != SlotCount)
        {
            Slots.Clear();
            for (int slot = SlotCount; slot >= 1; slot--)
            {
                Slots.Add(new JobSlotModel(slot));
            }
        }

        // 账上登记了这个 LoadPort 就以账为准；没登记（晶圆账没开）才退回按 Mapping 结果，那时没有片号。
        bool useLedger = dto.LedgerSlots.Count > 0;
        foreach (var row in Slots)
        {
            string waferId = string.Empty;
            var state = JobWaferState.None;
            if (useLedger)
            {
                var wafer = dto.LedgerSlots.FirstOrDefault(item => item.Slot == row.Slot)?.Wafer;
                if (wafer is not null)
                {
                    waferId = wafer.WaferId;
                    state = JobSlotModel.StateOf(wafer);
                }
            }
            else
            {
                var mapping = dto.Slots.FirstOrDefault(item => item.Slot == row.Slot);
                if (mapping is not null)
                {
                    state = JobSlotModel.StateOf(mapping.State);
                }
            }

            if (row.Update(waferId, state))
            {
                row.Sequence = Sequence;
            }
        }
    }
}
