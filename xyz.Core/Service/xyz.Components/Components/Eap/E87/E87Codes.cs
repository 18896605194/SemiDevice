namespace xyz.Components.Components;

/// <summary>
/// CAACK：E87 回 Host 的应答码（S3F18 载具动作、S3F26 端口动作、S3F28 改存取方式），数值照 SEMI E87；只列用到的。
/// </summary>
internal enum E87Caack : byte
{
    /// <summary>0 收下。</summary>
    Ok = 0,

    /// <summary>1 没有这个动作（或不支持）。</summary>
    InvalidCommand = 1,

    /// <summary>2 现在做不了：不是 ON-LINE REMOTE，或端口正在动作。</summary>
    CannotPerformNow = 2,

    /// <summary>3 数据、参数不对。</summary>
    InvalidData = 3,

    /// <summary>5 当前状态下不能做。</summary>
    InvalidState = 5,
}

/// <summary>
/// 载具事件带的原因（DV "Reason"）；只列用到的。
/// </summary>
internal enum E87Reason : byte
{
    /// <summary>0 槽图读到了，等 Host 核对（跟 #14 一起报）。</summary>
    VerificationNeeded = 0,

    /// <summary>5 Host 取消了这个载具（跟 #9、#16 一起报）。</summary>
    HostCancel = 5,
}

/// <summary>
/// E87 用到的其他协议常量。
/// </summary>
internal static class E87Codes
{
    /// <summary>S3F16 GRANT：0 可以发。</summary>
    public const byte Granted = 0;

    /// <summary>载具号是 E39 的 ObjID：最长 80 个字符。</summary>
    public const int MaxObjectIdLength = 80;

    /// <summary>载具号（E39 ObjID）不能有这几个字符。</summary>
    public const string ForbiddenIdChars = "?*~>:";
}
