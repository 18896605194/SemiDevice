namespace xyz.Modules;

/// <summary>
/// E84 握手分段的超时计时器：哪一段超时就带哪个值回调 IE84Callback.HandoffTimeout，本次交接中止。
/// 分段按 SEMI E84 的握手顺序，各段边界与时长以规范和设备手册为准（时长走 SC 配置）。
/// </summary>
public enum E84Timer
{
    /// <summary>置起 L_REQ/U_REQ 与 HO_AVBL 后，等搬运车请求交接（TR_REQ）。</summary>
    TP1 = 1,

    /// <summary>给出 READY 后，等搬运车开始动作（BUSY）。</summary>
    TP2,

    /// <summary>搬运车动作中，等载具实际放到位／取离端口。</summary>
    TP3,

    /// <summary>载具到位后，等搬运车给出完成信号（COMPT）。</summary>
    TP4,

    /// <summary>完成信号之后，等搬运车撤销交接信号。</summary>
    TP5,

    /// <summary>信号复位、本次交接收尾。</summary>
    TP6,
}
