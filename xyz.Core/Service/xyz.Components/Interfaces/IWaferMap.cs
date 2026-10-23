using xyz.Components.Components;
using xyz.Components.Models;
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
    HandleResult<ModuleRoutine> Map(string station);

    /// <summary>
    /// 各站点最近一次扫到的结果（槽图 + 设备回的原文），键是站点名；开扫时先删掉这个站的旧结果
    /// </summary>
    IReadOnlyDictionary<string, WaferMapResult> MapResults { get; }
}
