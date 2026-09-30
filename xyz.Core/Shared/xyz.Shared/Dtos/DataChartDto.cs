using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 数据曲线（实时曲线也用）的一个信号。名字全部由 sc.xml 生成、按点号分层（模块 → 部件 → 属性），界面照它长出勾选树：
/// SV = 组件全路径.属性名；组件绑的 IO = 组件全路径.配置项名去掉 Index（Chamber1.Door.DiOpened）。
/// 名字就是库表里的列名，查询按名字查。
/// </summary>
public class DataChartSignalDto
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 开关量（DI/DO、布尔 SV）：值只有 1/0（true=1、false=0），界面在图的下方分道画。
    /// </summary>
    public bool IsDigital { get; set; }

    /// <summary>
    /// 来源：SV / DI / DO / AI / AO。
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// IO 点的地址（DI100 这种）；SV 为空。
    /// </summary>
    public string Address { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// 说明：SV 的描述；IO 是点表里的点名和描述。
    /// </summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 数据曲线能选哪些信号（RpcResponse.Data 的 JSON）：就是正在记的那些——sc.xml 里配置了的（组件上的 SV + 组件绑的 IO），
/// 顺序跟 sc.xml 一样；库里记的也正是这些。
/// </summary>
public class DataChartSignalsDto
{
    public List<DataChartSignalDto> Signals { get; set; } = [];

    /// <summary>
    /// 最多同时画几条曲线（sc.xml 的 DataChart 节点 QueryMaxSignals）。
    /// </summary>
    public int MaxSignals { get; set; }

    /// <summary>
    /// 采样周期（毫秒）。
    /// </summary>
    public int SampleIntervalMs { get; set; }
}

/// <summary>
/// 数据曲线查询：按名字查 [Start, End) 的曲线。时间用本机时间，传过去只保留刻度。
/// </summary>
[ProtoContract]
public class DataChartQuery
{
    /// <summary>
    /// 起始时刻（含）。
    /// </summary>
    [ProtoMember(1)]
    public DateTime Start { get; set; }

    /// <summary>
    /// 截止时刻（不含）。
    /// </summary>
    [ProtoMember(2)]
    public DateTime End { get; set; }

    /// <summary>
    /// 信号名；超过最多条数的只查前面的。
    /// </summary>
    [ProtoMember(3)]
    public List<string> Names { get; set; } = [];
}

/// <summary>
/// 数据曲线查询结果（RpcResponse.Data 的 JSON）：所有曲线共用一条时间轴。
/// </summary>
public class DataChartResultDto
{
    /// <summary>
    /// 时间轴，UTC 毫秒（Unix 时间戳）。
    /// </summary>
    public List<long> Times { get; set; } = [];

    public List<DataChartSeriesDto> Series { get; set; } = [];

    /// <summary>
    /// 点数超过上限时抽稀过：每段（BucketMs 宽）只留最小、最大两个点，峰谷不丢。
    /// </summary>
    public bool IsDecimated { get; set; }

    public long BucketMs { get; set; }
}

/// <summary>
/// 一条曲线：Values 跟 Times 一一对应，null = 这一刻没数据（曲线断开）；
/// Min / Max / Avg / Count 是查询时间段内原始数据的精确统计（抽不抽稀都一样）。
/// </summary>
public class DataChartSeriesDto
{
    public string Name { get; set; } = string.Empty;

    public List<float?> Values { get; set; } = [];

    public double? Min { get; set; }

    public double? Max { get; set; }

    public double? Avg { get; set; }

    public int Count { get; set; }
}
