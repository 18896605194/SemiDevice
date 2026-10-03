namespace xyz.Components.Enums;

/// <summary>
/// 人工调整晶圆账（移动、删除、新建）的结果。
/// </summary>
public enum WaferAdjustResult
{
    /// <summary>改好了。</summary>
    Ok,

    /// <summary>晶圆账没开（IsEnable=False）。</summary>
    Disabled,

    /// <summary>账上没有这个位置（模块没登记过槽位）。</summary>
    LocationNotFound,

    /// <summary>槽号超出这个位置的槽数。</summary>
    SlotOutOfRange,

    /// <summary>源槽上没片（可能刚被别处改过账）。</summary>
    NoWafer,

    /// <summary>目标槽上已经有片。</summary>
    SlotOccupied,

    /// <summary>源和目标是同一个槽。</summary>
    SameSlot,

    /// <summary>新建时没填片号。</summary>
    WaferIdRequired,

    /// <summary>新建时这个片号已经在账上别的槽里（要挪就用移动）。</summary>
    DuplicateWaferId,
}
