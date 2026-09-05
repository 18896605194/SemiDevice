namespace xyz.Shared.Dtos;


public class LoadPortDto
{
    /// <summary>模块实例名，与 EventBus token 一致，如 "LoadPort1"。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>模块状态码，取值见 ModuleState/LoadPortState。</summary>
    public int State { get; set; }

    /// <summary>驱动串口连接是否可用。</summary>
    public bool IsConnected { get; set; }

    /// <summary>FOUP 是否在位（PODON/PODOF 事件刷新）。</summary>
    public bool IsPodPlaced { get; set; }

    /// <summary>花篮槽位表（Mapping 结果），下标顺序即槽位顺序。</summary>
    public List<LoadPortSlotDto> Slots { get; set; } = [];

    /// <summary>载具 ID，来自 LoadPort 内部 RFID 组件；未读到为空串。</summary>
    public string CarrierId { get; set; } = string.Empty;
}

/// <summary>
/// LoadPort 单个槽位的契约对象，与前端 Presentation 的 LoadPortSlot 对应。
/// </summary>
public class LoadPortSlotDto
{
    /// <summary>槽位号，从 1 开始。</summary>
    public int Slot { get; set; }

    /// <summary>该槽位是否有晶圆。</summary>
    public bool HasWafer { get; set; }
}
