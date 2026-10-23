namespace xyz._35021.Module.Drivers.Fcd;

/// <summary>
/// 富创得 LoadPort（LP300）B 类帧协议：指令类型前缀、回复类型（槽图字符在取槽图指令 FcdGetMapCommand 里认）
/// </summary>
public static class FcdProtocol
{
    #region 指令类型前缀（3 字符）

    /// <summary>
    /// 查询类指令
    /// </summary>
    public const string Get = "GET";

    /// <summary>
    /// 动作类指令
    /// </summary>
    public const string Move = "MOV";

    /// <summary>
    /// 设置类指令
    /// </summary>
    public const string Set = "SET";

    #endregion

    #region 回复类型

    /// <summary>
    /// 受理（查询类的数据随它回、它就是收尾）
    /// </summary>
    public const string Ack = "ACK";

    /// <summary>
    /// 动作做完 / 主动事件
    /// </summary>
    public const string Inf = "INF";

    /// <summary>
    /// 异常结束
    /// </summary>
    public const string Abs = "ABS";

    /// <summary>
    /// 拒绝
    /// </summary>
    public const string Nak = "NAK";

    #endregion
}
