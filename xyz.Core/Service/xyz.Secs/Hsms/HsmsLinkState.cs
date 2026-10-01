namespace xyz.Secs.Hsms;

/// <summary>
/// HSMS 链路状态（SEMI E37）：NOT CONNECTED → (TCP 连上) → CONNECTED NOT SELECTED → (Select 成功) → SELECTED。
/// 只有 SELECTED 才允许收发 SECS-II 数据消息。
/// </summary>
public enum HsmsLinkState
{
    /// <summary>TCP 未连或已断开。</summary>
    NotConnected = 0,

    /// <summary>TCP 已连但还没过 Select 交接（Passive 方受 T7 约束：超时未收到 Select.req 就断开）。</summary>
    ConnectedNotSelected = 1,

    /// <summary>Select 成功，可以收发数据消息。</summary>
    Selected = 2,
}
