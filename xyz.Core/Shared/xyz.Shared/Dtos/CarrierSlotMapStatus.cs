namespace xyz.Shared.Dtos;

/// <summary>
/// 载具槽图的认定状态（E87 SlotMapStatus）。
/// </summary>
public enum CarrierSlotMapStatus
{
    /// <summary>还没做 Mapping。</summary>
    NotRead = 0,

    /// <summary>已读到槽图，尚未与 Host 核对。</summary>
    Read = 1,

    /// <summary>已上报，等 Host 确认。</summary>
    WaitingForHost = 2,

    /// <summary>Host 确认与派工单一致。</summary>
    Verified = 3,

    /// <summary>Host 判定不一致（片数/位置对不上）。</summary>
    VerifyFailed = 4,
}
