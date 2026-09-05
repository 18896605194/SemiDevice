namespace xyz.Drivers.Loadport.FCD;

/// <summary>
/// FCD B 类帧协议常量：指令类型前缀与回复类型。
/// </summary>
public static class FcdProtocol
{
    #region 指令类型前缀（3 字符）

    /// <summary>查询类指令。</summary>
    public const string Get = "GET";

    /// <summary>动作类指令。</summary>
    public const string Move = "MOV";

    /// <summary>设置类指令。</summary>
    public const string Set = "SET";

    #endregion

    #region 回复类型

    /// <summary>受理。</summary>
    public const string Ack = "ACK";

    /// <summary>完成通知 / 主动事件。</summary>
    public const string Inf = "INF";

    /// <summary>异常完成。</summary>
    public const string Abs = "ABS";

    /// <summary>拒绝。</summary>
    public const string Nak = "NAK";

    #endregion
}
