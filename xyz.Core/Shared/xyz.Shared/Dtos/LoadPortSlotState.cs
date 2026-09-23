namespace xyz.Shared.Dtos;

/// <summary>
/// 花篮单个槽位的 Mapping 结果（厂商无关语义，与驱动层 SlotState 取值一一对应）。
/// </summary>
public enum LoadPortSlotState
{
    /// <summary>无法识别。</summary>
    Undefined = 0,

    /// <summary>空槽。</summary>
    Empty = 1,

    /// <summary>有片（设备分辨不出放置是否正确时使用）。</summary>
    NotEmpty = 2,

    /// <summary>有片且放置正确。</summary>
    CorrectlyOccupied = 3,

    /// <summary>双片（叠片）。</summary>
    DoubleSlotted = 4,

    /// <summary>交叉片（跨槽）。</summary>
    CrossSlotted = 5,
}
