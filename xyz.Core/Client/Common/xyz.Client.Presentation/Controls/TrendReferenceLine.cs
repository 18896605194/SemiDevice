namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 趋势图上的四条基准线：X 两条竖线、Y 两条横线。
/// </summary>
public enum TrendReferenceLine
{
    /// <summary>
    /// 没有（鼠标下面没有线、没在拖线）。
    /// </summary>
    None,

    /// <summary>
    /// 第一条竖线。
    /// </summary>
    X1,

    /// <summary>
    /// 第二条竖线。
    /// </summary>
    X2,

    /// <summary>
    /// 第一条横线。
    /// </summary>
    Y1,

    /// <summary>
    /// 第二条横线。
    /// </summary>
    Y2,
}
