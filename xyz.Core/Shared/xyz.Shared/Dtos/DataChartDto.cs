namespace xyz.Shared.Dtos;

/// <summary>
/// 数据曲线（实时曲线也用）的一个信号。名字全部由 machine.xml 生成、按两个下划线 __ 分层（模块 → 部件 → 属性），界面照它长出勾选树：
/// SV = 元件全路径 + 属性名；元件绑的 IO = 元件全路径 + 配置项名去掉 Index（Chamber1__Door__DiOpened）。
/// 名字就是库表里的列名，查询按名字查；不用点号是因为 SqlSugar 会把点号拆成"表.列"。
/// </summary>
public class DataChartSignalDto
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 开关量（DI/DO、布尔 SV）：值只有 1/0（true=1、false=0），界面画成阶梯线。
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
/// 数据曲线能选哪些信号（RpcResponse.Data 的 JSON）：就是正在记的那些——machine.xml 里配置了的（元件上的 SV + 元件绑的 IO），
/// 顺序跟 machine.xml 一样；库里记的也正是这些。
/// </summary>
public class DataChartSignalsDto
{
    public List<DataChartSignalDto> Signals { get; set; } = [];

    /// <summary>
    /// 最多同时画几条曲线（machine.xml 的 DataChart 节点 QueryMaxSignals）。
    /// </summary>
    public int MaxSignals { get; set; }

    /// <summary>
    /// 采样周期（毫秒）。
    /// </summary>
    public int SampleIntervalMs { get; set; }

    /// <summary>
    /// 截图存到哪个文件夹（machine.xml 的 DataChart 节点 ScreenshotDirectory；相对路径算客户端程序目录）。
    /// </summary>
    public string ScreenshotDirectory { get; set; } = string.Empty;
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
/// 导出流里的一条（RpcResponse.Data 的 JSON）：一段时间的原始行，不抽稀。Values[i] 是 Names[i] 那条曲线，跟 Times 一一对应，null = 这一刻没读到。
/// </summary>
public class DataChartExportDto
{
    /// <summary>
    /// 时间轴，UTC 毫秒（Unix 时间戳）。
    /// </summary>
    public List<long> Times { get; set; } = [];

    public List<string> Names { get; set; } = [];

    public List<List<double?>> Values { get; set; } = [];
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
