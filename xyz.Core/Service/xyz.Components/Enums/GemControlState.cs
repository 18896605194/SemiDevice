namespace xyz.Components.Enums;

/// <summary>
/// GEM 控制状态（SEMI E30 Control State Model）：设备听不听 Host 的。数值就是 E30 的 ControlState，上报 Host 用这个数。
/// OFF-LINE 三个子状态里 Host 只能做通讯建立和请求上线；ON-LINE LOCAL 时 Host 能查、不能下动作命令；ON-LINE REMOTE 才全听 Host 的。
/// </summary>
public enum GemControlState : byte
{
    /// <summary>OFF-LINE / EQUIPMENT OFF-LINE：操作员让设备离线，Host 请求上线也不行（回 ONLACK=1）。</summary>
    EquipmentOffline = 1,

    /// <summary>OFF-LINE / ATTEMPT ON-LINE：操作员点了上线，设备发 S1F1 问 Host，等回复。</summary>
    AttemptOnline = 2,

    /// <summary>OFF-LINE / HOST OFF-LINE：设备愿意上线，等 Host 发 S1F17 请求上线。</summary>
    HostOffline = 3,

    /// <summary>ON-LINE / LOCAL：在线，但操作员在机台上操作；Host 的动作命令不收。</summary>
    OnlineLocal = 4,

    /// <summary>ON-LINE / REMOTE：在线，Host 控制。</summary>
    OnlineRemote = 5,
}
