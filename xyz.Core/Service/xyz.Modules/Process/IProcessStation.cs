using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 能做工艺的站点：工艺配方、流程配方、Job 按它认（腔体、以后的热板冷板都实现它）
/// </summary>
public interface IProcessStation : IModule
{
    /// <summary>
    /// 站点类型：工艺配方按它挑能跑的站点
    /// </summary>
    string Type { get; }

    /// <summary>
    /// 正在跑的工艺请求；没在跑为 null
    /// </summary>
    ProcessRequest? CurrentProcess { get; }

    /// <summary>
    /// 这个配方在这个站点上跑不跑得了（配方里选的设备这里有没有）：对得上返回 null，对不上回字段和值。不看状态，不动设备
    /// </summary>
    (string Field, string Value)? FindRecipeMismatch(RecipeData recipe);

    /// <summary>
    /// 现在能不能起这个工艺（只查不动）：状态 → 资源 → 互锁，能起返回 null，不能回原因（错误码 + 参数）
    /// </summary>
    HandleResult? CheckProcess(ProcessRequest request);

    /// <summary>
    /// 起工艺：跟 <see cref="CheckProcess"/> 同一套检查，都过了才挂上工艺动作；被拒带原因返回，站点不动
    /// </summary>
    HandleResult<ModuleOperation> StartProcess(ProcessRequest request);

    /// <summary>
    /// 今天开工了几片（中止、失败的也算）；按天存库，开机读回来接着数
    /// </summary>
    int TodayWafers { get; }

    /// <summary>
    /// 累计开工了几片：库里各天的加起来
    /// </summary>
    long TotalWafers { get; }

    /// <summary>
    /// 最近一次开工的时间戳（Stopwatch 开机以来的计时，改系统时间不影响），只用来比先后、只在内存；这次开机还没开过工为空。调度轮着挑并行的站用
    /// </summary>
    long? LastStartedTimestamp { get; }

    /// <summary>
    /// 当前工艺进度 0~100，只在内存；没在做工艺为空
    /// </summary>
    int? ProcessPercent { get; }
}
