namespace xyz.Shared.Dtos;


public class LoadPortDto
{
    /// <summary>模块实例名，与 EventBus token 一致，如 "LoadPort1"。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>模块状态码，取值见 ModuleState/LoadPortState。</summary>
    public int State { get; set; }

    /// <summary>模块模式（Online/Offline）：是否参与自动调度。</summary>
    public ModuleMode Mode { get; set; }

    /// <summary>驱动串口连接是否可用。</summary>
    public bool IsConnected { get; set; }

    /// <summary>载具到了：后台按 sc.xml PresenceSource 判出来的（状态查询或 PODON/PODOF），跟后台判载具到达、拿走用的是同一个；原始的两位是 IsPresent / IsPlaced。</summary>
    public bool IsCarrierArrived { get; set; }

    /// <summary>查询反馈：FOUP 在位；null 表示反馈不可用。</summary>
    public bool? IsPresent { get; set; }

    /// <summary>查询反馈：FOUP 放置到位；null 表示反馈不可用。</summary>
    public bool? IsPlaced { get; set; }

    /// <summary>查询反馈：门开到位；null 表示反馈不可用。</summary>
    public bool? IsDoorOpen { get; set; }

    /// <summary>查询反馈：门关到位；null 表示反馈不可用。</summary>
    public bool? IsDoorClosed { get; set; }

    /// <summary>查询反馈：设备硬件报警；null 表示反馈不可用。</summary>
    public bool? IsDeviceAlarm { get; set; }

    /// <summary>Auto/Manual（LoadPort 的 Access Mode）：true = Auto（搬运车经 E84 自动交接），false = Manual（人工放取）。</summary>
    public bool AutoMode { get; set; }

    /// <summary>花篮槽数（sc.xml 里 LoadPort 节点的 SlotCount），界面按它画槽位。</summary>
    public int SlotCount { get; set; }

    /// <summary>花篮槽位表（设备 Mapping 结果），下标顺序即槽位顺序；取放片、人工改账之后它不变，界面画片以 LedgerSlots 为准。</summary>
    public List<LoadPortSlotDto> Slots { get; set; } = [];

    /// <summary>
    /// 晶圆账上各槽的片（空槽的 Wafer 为 null）；晶圆账没开或没登记这个 LoadPort 时为空，界面这时才退回按 Mapping 结果画。
    /// </summary>
    public List<WaferSlotDto> LedgerSlots { get; set; } = [];

    /// <summary>载具 ID，来自 LoadPort 内部 RFID 组件；未读到为空串。</summary>
    public string CarrierId { get; set; } = string.Empty;

    /// <summary>端口上有没有载具。载具 ID 为空只说明没读到，不代表没载具。</summary>
    public bool HasCarrier { get; set; }

    /// <summary>批次号；未下发为空串。</summary>
    public string LotId { get; set; } = string.Empty;

    /// <summary>载具 ID 认定到哪一步。</summary>
    public CarrierIdStatus CarrierIdStatus { get; set; }

    /// <summary>槽图认定到哪一步。</summary>
    public CarrierSlotMapStatus CarrierSlotMapStatus { get; set; }

    /// <summary>载具取放到哪一步。</summary>
    public CarrierAccessStatus CarrierAccessStatus { get; set; }

    /// <summary>
    /// 比较当前发布的模块状态、连接状态、设备反馈、载具 ID、花篮槽位与账上的片；没有上一次状态时视为变化。
    /// </summary>
    public bool HasStateChanged(LoadPortDto? previous)
    {
        if (previous is null)
        {
            return true;
        }

        if (Name != previous.Name)
        {
            return true;
        }

        if (State != previous.State)
        {
            return true;
        }

        if (Mode != previous.Mode)
        {
            return true;
        }

        if (IsConnected != previous.IsConnected)
        {
            return true;
        }

        if (IsCarrierArrived != previous.IsCarrierArrived)
        {
            return true;
        }

        if (IsPresent != previous.IsPresent)
        {
            return true;
        }

        if (IsPlaced != previous.IsPlaced)
        {
            return true;
        }

        if (IsDoorOpen != previous.IsDoorOpen)
        {
            return true;
        }

        if (IsDoorClosed != previous.IsDoorClosed)
        {
            return true;
        }

        if (IsDeviceAlarm != previous.IsDeviceAlarm)
        {
            return true;
        }

        if (AutoMode != previous.AutoMode)
        {
            return true;
        }

        if (CarrierId != previous.CarrierId)
        {
            return true;
        }

        if (HasCarrier != previous.HasCarrier
            || LotId != previous.LotId
            || CarrierIdStatus != previous.CarrierIdStatus
            || CarrierSlotMapStatus != previous.CarrierSlotMapStatus
            || CarrierAccessStatus != previous.CarrierAccessStatus)
        {
            return true;
        }

        if (Slots.Count != previous.Slots.Count)
        {
            return true;
        }

        for (int i = 0; i < Slots.Count; i++)
        {
            if (Slots[i].Slot != previous.Slots[i].Slot || Slots[i].State != previous.Slots[i].State)
            {
                return true;
            }
        }

        return !WaferSlotDto.SameSlots(LedgerSlots, previous.LedgerSlots);
    }
}
