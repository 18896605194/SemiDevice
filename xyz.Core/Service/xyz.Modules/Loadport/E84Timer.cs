namespace xyz.Modules;

/// <summary>
/// E84 握手分段的超时计时器
/// </summary>
public enum E84Timer
{
    /// <summary>置起 L_REQ/U_REQ 与 HO_AVBL 后，等搬运车请求交接（TR_REQ）。</summary>
    TP1 = 1,

    /// <summary>给出 READY 后，等搬运车开始动作（BUSY）。</summary>
    TP2,

    /// <summary>搬运车动作中，等载具实际放到位／取离端口。</summary>
    TP3,

    /// <summary>载具放上/取走、撤掉 L_REQ/U_REQ 后，等搬运车撤掉 BUSY、给出 COMPT。</summary>
    TP4,

    /// <summary>COMPT 来了撤掉 READY 后，等搬运车撤销交接信号（VALID）。</summary>
    TP5,

    /// <summary>连续交接时 VALID 撤掉后等下一次 VALID；当前流程不做连续交接，不用这一段。</summary>
    TP6,
}
