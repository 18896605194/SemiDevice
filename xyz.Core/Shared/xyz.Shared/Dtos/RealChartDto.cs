using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 实时曲线的信号表（RpcResponse.Data 的 JSON）：每帧的值按 Signals 的顺序排。
/// </summary>
public class RealChartLayoutDto
{
    /// <summary>
    /// 这一轮采样的标识：帧上带的对不上，说明后端重启过，信号表要重新拉。
    /// </summary>
    public long Session { get; set; }

    public int SampleIntervalMs { get; set; }

    /// <summary>
    /// 后端内存里留最近多少秒，页面能选的最长显示范围。
    /// </summary>
    public int WindowSeconds { get; set; }

    /// <summary>
    /// 最多同时画几条曲线（跟数据曲线同一个设置）。
    /// </summary>
    public int MaxSignals { get; set; }

    public List<DataChartSignalDto> Signals { get; set; } = [];
}

/// <summary>
/// 要这几个信号最近一段的值（实时曲线页打开、新勾曲线时补历史）。
/// </summary>
[ProtoContract]
public class RealChartRecentQuery
{
    [ProtoMember(1)]
    public List<string> Names { get; set; } = [];
}

/// <summary>
/// 最近一段（RpcResponse.Data 的 JSON）：Times 是 UTC 毫秒，每条曲线的值跟 Times 一一对应；不在信号表里的名字不返回。
/// </summary>
public class RealChartRecentDto
{
    public long Session { get; set; }

    public List<long> Times { get; set; } = [];

    public List<DataChartSeriesDto> Series { get; set; } = [];
}

/// <summary>
/// 实时曲线的一帧：一个采样周期一帧，后端主动推（EventBus，token = EventToken，不留存）。
/// Values 按信号表（RealChartLayoutDto.Signals）的顺序，null = 读不到。
/// </summary>
public class RealChartFrameDto
{
    public const string EventToken = "RealChart";

    public long Session { get; set; }

    /// <summary>
    /// UTC 毫秒。
    /// </summary>
    public long Time { get; set; }

    public List<float?> Values { get; set; } = [];
}
