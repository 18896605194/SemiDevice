using xyz.Drivers.Loadport;

namespace xyz.Elements.Models;

/// <summary>
/// 扫一个站点的结果：解好的槽图 + 设备回的原文
/// </summary>
public sealed class WaferMapResult
{
    /// <summary>
    /// 槽图，下标 0 是第 1 槽
    /// </summary>
    public IReadOnlyList<SlotState> Slots { get; init; } = [];

    /// <summary>
    /// 设备回的原文（各家自己的写法，平台不解析），写日志、对照设备用
    /// </summary>
    public string Raw { get; init; } = string.Empty;
}
