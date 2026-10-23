using System.Globalization;

namespace xyz._35021.Module.Drivers.Reje;

/// <summary>
/// 锐洁协议：回复帧 &gt;错误码#内容@回显名、主动推送的回显名、取放坐标编码
/// </summary>
public static class RejeProtocol
{
    #region 回复帧

    /// <summary>
    /// 回复前缀；单独一个 '>' 是确认帧（不带回显名，只表示受理）
    /// </summary>
    public const string ResponsePrefix = ">";

    /// <summary>
    /// 成功码
    /// </summary>
    public const string SuccessCode = "00000000";

    #endregion

    #region 主动推送（回显名）

    /// <summary>
    /// 事件推送，比如手指在位变化
    /// </summary>
    public const string EventName = "Event";

    /// <summary>
    /// 主动报错推送（AEO 开着时）
    /// </summary>
    public const string ErrorName = "Error";

    /// <summary>
    /// 手指在位变化事件：SubWaferEx,&lt;手指&gt;,&lt;0有片|1无片&gt;
    /// </summary>
    public const string WaferEventName = "SubWaferEx";

    #endregion

    #region 推送怎么认

    /// <summary>
    /// 是不是手指在位推送：SubWaferEx,&lt;手指&gt;,&lt;0有片|1无片&gt;（注意 0 是有片）
    /// </summary>
    public static bool TryReadWaferEvent(RejeMessage message, out int arm, out bool hasWafer)
    {
        arm = 0;
        hasWafer = false;
        if (!string.Equals(message.Name, EventName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] parts = message.Content.Split(',');
        if (parts.Length != 3
            || !string.Equals(parts[0].Trim(), WaferEventName, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out arm))
        {
            return false;
        }

        string flag = parts[2].Trim();
        if (flag != "0" && flag != "1")
        {
            return false;
        }

        hasWafer = flag == "0";
        return true;
    }

    /// <summary>
    /// 是不是报错推送；成功码的 Error 帧是"没有报错"（多半是查报错超时作废以后迟到的回复），不算
    /// </summary>
    public static bool IsErrorPush(RejeMessage message)
    {
        return string.Equals(message.Name, ErrorName, StringComparison.OrdinalIgnoreCase)
               && message.Code != SuccessCode;
    }

    #endregion

    #region 编码

    /// <summary>
    /// 取放坐标编码 XYYZZ：手指 1 位十进制，工位与层各 2 位十六进制；任一段越界都会改变报文长度、设备会切出另一个坐标，所以下发前直接拒
    /// </summary>
    internal static string EncodeMotionArgs(int arm, int station, int slot)
    {
        if (arm < 1 || arm > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(arm), arm, "手指号须为 1-9。");
        }

        if (station < 0 || station > 0xFF)
        {
            throw new ArgumentOutOfRangeException(nameof(station), station, "工位号须为 0-255。");
        }

        if (slot < 0 || slot > 0xFF)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "层号须为 0-255。");
        }

        return arm.ToString(CultureInfo.InvariantCulture)
               + station.ToString("X2", CultureInfo.InvariantCulture)
               + slot.ToString("X2", CultureInfo.InvariantCulture);
    }

    #endregion
}
