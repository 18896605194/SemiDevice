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

    /// <summary>兼容的 FOUP 在位值，来自状态查询或 PODON/PODOF 事件。</summary>
    public bool IsPodPlaced { get; set; }

    /// <summary>查询反馈：FOUP 在位；null 表示反馈不可用。</summary>
    public bool? PodPresent { get; set; }

    /// <summary>查询反馈：FOUP 放置到位；null 表示反馈不可用。</summary>
    public bool? PodPlaced { get; set; }

    /// <summary>查询反馈：门开到位；null 表示反馈不可用。</summary>
    public bool? DoorOpen { get; set; }

    /// <summary>查询反馈：门关到位；null 表示反馈不可用。</summary>
    public bool? DoorClosed { get; set; }

    /// <summary>查询反馈：设备硬件报警；null 表示反馈不可用。</summary>
    public bool? DeviceAlarm { get; set; }

    /// <summary>Auto/Manual（LoadPort 的 Access Mode）：true = Auto（搬运车经 E84 自动交接），false = Manual（人工放取）。</summary>
    public bool AutoMode { get; set; }

    /// <summary>花篮槽数（sc.xml 里 LoadPort 节点的 SlotCount），界面按它画槽位。</summary>
    public int SlotCount { get; set; }

    /// <summary>花篮槽位表（Mapping 结果），下标顺序即槽位顺序。</summary>
    public List<LoadPortSlotDto> Slots { get; set; } = [];

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
    /// 比较当前发布的模块状态、连接状态、设备反馈、载具 ID 与花篮槽位；没有上一次状态时视为变化。
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

        if (IsPodPlaced != previous.IsPodPlaced)
        {
            return true;
        }

        if (PodPresent != previous.PodPresent)
        {
            return true;
        }

        if (PodPlaced != previous.PodPlaced)
        {
            return true;
        }

        if (DoorOpen != previous.DoorOpen)
        {
            return true;
        }

        if (DoorClosed != previous.DoorClosed)
        {
            return true;
        }

        if (DeviceAlarm != previous.DeviceAlarm)
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

        return false;
    }
}

/// <summary>
/// LoadPort 单个槽位的契约对象，与前端 Presentation 的 LoadPortSlot 对应。
/// </summary>
public class LoadPortSlotDto
{
    /// <summary>槽位号，从 1 开始。</summary>
    public int Slot { get; set; }

    /// <summary>该槽位的 Mapping 结果。</summary>
    public LoadPortSlotState State { get; set; }

    /// <summary>该槽位有没有片（叠片、交叉片也算有片）。</summary>
    public bool HasWafer =>
        State is LoadPortSlotState.NotEmpty or LoadPortSlotState.CorrectlyOccupied
            or LoadPortSlotState.DoubleSlotted or LoadPortSlotState.CrossSlotted;
}
