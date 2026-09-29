namespace xyz.Shared.Dtos;


public class ChamberDto
{
    /// <summary>模块实例名，与 EventBus token 一致，如 "Chamber1"。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>模块状态码，取值见 ModuleState/TransferModuleState/ChamberState。</summary>
    public int State { get; set; }

    /// <summary>模块模式（Online/Offline）：是否参与自动调度。</summary>
    public ModuleMode Mode { get; set; }

    /// <summary>是否启用（sc.xml 腔体节点的 IsEnable）：False = 装机未接/停用，动作一律发不出去。</summary>
    public bool IsEnable { get; set; }

    /// <summary>设备当前报错（错误码#内容）；无报错或腔体停用为 null。</summary>
    public string? DeviceError { get; set; }

    /// <summary>最近一次发起成功的工艺配方名；还没做过工艺为 null。</summary>
    public string? Recipe { get; set; }

    /// <summary>片位数（sc.xml 腔体节点的 SlotCount，一般 1），界面按它画片位。</summary>
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
                || Slots[i].WaferId != previous.Slots[i].WaferId)
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
    /// <summary>片位号，从 1 开始。</summary>
    public int Slot { get; set; }

    /// <summary>这个片位上的片（晶圆账）。</summary>
    public ChamberSlotState State { get; set; }

    /// <summary>片号（晶圆账的业务片号）；空片位为 null。</summary>
    public string? WaferId { get; set; }

    /// <summary>这个片位有没有片。</summary>
    public bool HasWafer => State != ChamberSlotState.Empty;
}
