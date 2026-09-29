namespace xyz.Shared.Dtos;

/// <summary>
/// 腔体单个片位上的片（晶圆账）：空，或有片及其工艺状态（与晶圆账 WaferProcessState 取值一一对应）。
/// </summary>
public enum ChamberSlotState
{
    /// <summary>空（账上这个片位没片）。</summary>
    Empty = 0,

    /// <summary>有片，还没做工艺。</summary>
    Idle = 1,

    /// <summary>有片，工艺中。</summary>
    InProcess = 2,

    /// <summary>有片，工艺做完了。</summary>
    Completed = 3,

    /// <summary>有片，工艺做失败了。</summary>
    Failed = 4,

    /// <summary>有片，工艺中途被中止。</summary>
    Aborted = 5,
}
