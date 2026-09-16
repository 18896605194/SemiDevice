namespace xyz.Shared.Dtos;

/// <summary>
/// 载具被取放到哪一步（E87 CarrierAccessingStatus）。
/// </summary>
public enum CarrierAccessStatus
{
    /// <summary>还没开始取放。</summary>
    NotAccessed = 0,

    /// <summary>门已开，机械手可以取放。</summary>
    InAccess = 1,

    /// <summary>这个载具的活干完了（由上层作业判定）。</summary>
    Complete = 2,

    /// <summary>取放中断（动作失败/人工中止），没干完。</summary>
    Stopped = 3,
}
