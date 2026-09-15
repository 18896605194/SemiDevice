namespace xyz.Drivers.Loadport;

public enum SlotState
{
    /// <summary>
    /// 无法识别。
    /// </summary>
    Undefined = 0,

    /// <summary>
    /// 空槽。
    /// </summary>
    Empty = 1,

    /// <summary>
    /// 有片（设备分辨不出放置是否正确时使用）。
    /// </summary>
    NotEmpty = 2,

    /// <summary>
    /// 有片且放置正确。
    /// </summary>
    CorrectlyOccupied = 3,

    /// <summary>
    /// 双片（叠片）。
    /// </summary>
    DoubleSlotted = 4,

    /// <summary>
    /// 交叉片（跨槽）。
    /// </summary>
    CrossSlotted = 5,
}
