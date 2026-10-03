namespace xyz.Client.Presentation.Models;

/// <summary>
/// 圆片上显示的字（WaferModel.LpSlot）：这片从哪个 LoadPort 的第几槽来的，写成"LoadPort1-25"。
/// 手臂上的片、腔体里的片都用这一份；来源不是 LoadPort（在手臂、腔体上补账建的）就不显示。
/// </summary>
public static class WaferLabel
{
    public static string Of(string? sourceLoadPort, int sourceSlot)
    {
        if (string.IsNullOrEmpty(sourceLoadPort))
        {
            return string.Empty;
        }

        return $"{sourceLoadPort}-{sourceSlot}";
    }
}
