namespace xyz.Components.Enums;

/// <summary>
/// GEM 通讯状态（SEMI E30 Communications State Model）：跟 Host 的通讯建立了没有。
/// 链路连上不等于建立了通讯——要 S1F13 / S1F14 换过一次（谁先发都行）才算 COMMUNICATING。
/// </summary>
public enum GemCommState : byte
{
    /// <summary>NOT COMMUNICATING：链路没连上，或者连上了还没换过 S1F13 / S1F14；设备每隔一段发一次 S1F13，Host 别的报文不理。</summary>
    NotCommunicating = 1,

    /// <summary>COMMUNICATING：通讯建立了，报文往来没有限制。</summary>
    Communicating = 2,
}
