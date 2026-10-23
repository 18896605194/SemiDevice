using xyz.Components.Components;
using xyz.Drivers.Loadport;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 能扫片（看料盒每一槽有没有片）：LoadPort 自带扫片传感器的、带扫片头的机械手实现；调用方按 is 判断
/// </summary>
public interface IWaferMap
{
    /// <summary>
    /// 扫这个站点：扫片 + 取图，取到的存进 MapResults
    /// </summary>
    HandleResult<ModuleOperation> Map(string station);

    /// <summary>
    /// 各站点最近一次扫到的槽图，键是站点名、下标 0 是第 1 槽；开扫时先删掉这个站的旧结果
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<SlotState>> MapResults { get; }
}
