using System.Globalization;

namespace xyz.Drivers.Robot.Reje;

/// <summary>
/// 锐洁协议常量与通用解析：回复帧 &gt;错误码#内容@回显名、确认帧 &gt;、主动推送回显名、取放坐标编码。
/// </summary>
public static class RejeProtocol
{
    #region 回复帧

    /// <summary>回复前缀；单独一个 '>' 是确认帧（不带回显名，只表示受理）。</summary>
    public const string ResponsePrefix = ">";

    /// <summary>成功码。</summary>
    public const string SuccessCode = "00000000";

    #endregion

    #region 主动推送（回显名）

    /// <summary>事件推送，如手指在位变化。</summary>
    public const string EventName = "Event";

    /// <summary>主动报错推送（AEO 开启时）。</summary>
    public const string ErrorName = "Error";

    /// <summary>心跳推送。</summary>
    public const string HeartBeatName = "HeartBeat";

    /// <summary>手指在位变化事件：SubWaferEx,&lt;手指&gt;,&lt;0有片|1无片&gt;。</summary>
    public const string WaferEventName = "SubWaferEx";

    #endregion

    #region 解析与编码

    /// <summary>
    /// 拆分回复帧（已去 ';'）为错误码、内容、回显名；确认帧或格式不符返回 false。
    /// </summary>
    internal static bool TrySplit(string body, out string code, out string content, out string name)
    {
        code = string.Empty;
        content = string.Empty;
        name = string.Empty;
        if (!body.StartsWith(ResponsePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string rest = body[ResponsePrefix.Length..];
        int hash = rest.IndexOf('#');
        int at = rest.LastIndexOf('@');
        if (hash < 0 || at <= hash || at >= rest.Length - 1)
        {
            return false;
        }

        code = rest[..hash];
        content = rest[(hash + 1)..at];
        name = rest[(at + 1)..];
        return true;
    }

    /// <summary>
    /// 取放坐标编码 XYYZZ：手指 1 位十进制，工位与层各 2 位十六进制。
    /// 任一段越界都会改变报文长度，设备会按错误切分解析出另一个坐标，因此下发前直接拒绝。
    /// </summary>
    internal static string EncodeMotionArgs(int arm, int station, int slot)
    {
        if (arm is < 1 or > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(arm), arm, "手指号须为 1-9。");
        }

        if (station is < 0 or > 0xFF)
        {
            throw new ArgumentOutOfRangeException(nameof(station), station, "工位号须为 0-255。");
        }

        if (slot is < 0 or > 0xFF)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "层号须为 0-255。");
        }

        return arm.ToString(CultureInfo.InvariantCulture)
               + station.ToString("X2", CultureInfo.InvariantCulture)
               + slot.ToString("X2", CultureInfo.InvariantCulture);
    }

    #endregion
}
