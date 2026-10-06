namespace xyz.Shared.Dtos;

/// <summary>
/// LoadPort 单个槽位的契约对象，与前端 Presentation 的 LoadPortSlot 对应。
/// </summary>
public class LoadPortSlotDto
{
    public int Slot { get; set; }

    public LoadPortSlotState State { get; set; }

    public bool HasWafer => State != LoadPortSlotState.Empty;
}
