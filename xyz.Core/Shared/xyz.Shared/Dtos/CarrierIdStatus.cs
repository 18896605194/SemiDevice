namespace xyz.Shared.Dtos;

/// <summary>
/// 载具 ID 的认定状态（E87 CarrierIDStatus）。
/// 读到 ≠ 认定：读到之后还要 Host 点头才算数，Host 说了不算的场合（未接 EAP）停在 Read。
/// </summary>
public enum CarrierIdStatus
{
    /// <summary>还没读。</summary>
    NotRead = 0,

    /// <summary>读到了，尚未与 Host 核对。</summary>
    Read = 1,

    /// <summary>读码失败（读头没读出来、标签空、超时）。</summary>
    ReadFailed = 2,

    /// <summary>已上报，等 Host 确认。</summary>
    WaitingForHost = 3,

    /// <summary>Host 确认一致。</summary>
    Verified = 4,

    /// <summary>Host 判定不一致。</summary>
    VerifyFailed = 5,
}
