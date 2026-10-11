namespace xyz.Shared.Dtos;


public class ChamberDto
{
    /// <summary>
    /// Chamber1
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// State
    /// </summary>
    public int State { get; set; }

    /// <summary>
    /// Online/Offline
    /// </summary>
    public ModuleMode Mode { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnable { get; set; }

    /// <summary>
    /// 设备当前报错
    /// </summary>
    public string? DeviceError { get; set; }

    /// <summary>
    /// 最近一次发起成功的工艺配方名
    /// </summary>
    public string? Recipe { get; set; }

    /// <summary>
    /// 片位数
    /// </summary>
    public int SlotCount { get; set; }

    /// <summary>各片位上的片（晶圆账），下标顺序即片位顺序。</summary>
    public List<ChamberSlotDto> Slots { get; set; } = [];

    /// <summary>
    /// 比较当前发布的模块状态、模式、启用、设备报错、配方与片位；没有上一次状态时视为变化。
    /// </summary>
    public bool HasStateChanged(ChamberDto? previous)
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

        if (IsEnable != previous.IsEnable)
        {
            return true;
        }

        if (DeviceError != previous.DeviceError)
        {
            return true;
        }

        if (Recipe != previous.Recipe)
        {
            return true;
        }

        if (SlotCount != previous.SlotCount)
        {
            return true;
        }

        if (Slots.Count != previous.Slots.Count)
        {
            return true;
        }

        for (int i = 0; i < Slots.Count; i++)
        {
            if (Slots[i].Slot != previous.Slots[i].Slot
                || Slots[i].State != previous.Slots[i].State
                || Slots[i].WaferId != previous.Slots[i].WaferId
                || Slots[i].SourceLoadPort != previous.Slots[i].SourceLoadPort
                || Slots[i].SourceSlot != previous.Slots[i].SourceSlot)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// 腔体单个片位的契约对象：片位号 + 账上的片。
/// </summary>
public class ChamberSlotDto
{
    /// <summary>
    /// 片位号，从 1 开始
    /// </summary>
    public int Slot { get; set; }

    /// <summary>这个片位上的片（晶圆账）。</summary>
    public ChamberSlotState State { get; set; }

    /// <summary>片号（晶圆账的业务片号）；空片位为 null。</summary>
    public string? WaferId { get; set; }

    /// <summary>来源 LoadPort 的模块名（如 LoadPort1）；空片位、不是在 LoadPort 上建的片为 null。圆片上显示"LoadPort1-25"用。</summary>
    public string? SourceLoadPort { get; set; }

    /// <summary>来源 LoadPort 的槽号；空片位、不是在 LoadPort 上建的片为 0。</summary>
    public int SourceSlot { get; set; }

    /// <summary>这个片位有没有片。</summary>
    public bool HasWafer => State != ChamberSlotState.Empty;
}
