namespace xyz.Drivers.Loadport;

/// <summary>
/// 槽位状态（厂商无关，取值与 SEMI E87 SlotState 一致）：
/// 各品牌 Mapping 字符归一化到这里，模块/EAP/界面只认本类型。
/// </summary>
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
