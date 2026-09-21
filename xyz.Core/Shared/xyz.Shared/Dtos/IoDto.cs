namespace xyz.Shared.Dtos;

/// <summary>
/// IO 点位契约：后端按周期整包推，界面订阅后照 类型 → 模块 两级分组显示。
/// 点表里加了新点、改了归属，界面自己就跟着变——不用改界面代码。
/// </summary>
public class IoDto
{
    public const string EventToken = "Io";

    /// <summary>
    /// 采集在不在工作（PLC 连着才算）。断了以后各点的 IsValid 全是 false，
    /// 界面该把值显示成灰的，别让人以为读的是实时值。
    /// </summary>
    public bool IsCollecting { get; set; }

    public List<IoTypeDto> Types { get; set; } = [];
}

/// <summary>
/// 一类 IO（DI / DO / AI / AO）。
/// </summary>
public class IoTypeDto
{
    public string Type { get; set; } = string.Empty;

    /// <summary>这一类里的点按归属模块分组，模块名来自点表的 Module 列。</summary>
    public List<IoModuleDto> Modules { get; set; } = [];
}

/// <summary>
/// 一个模块（LoadPort1、Chamber1、E84……）名下的点。
/// </summary>
public class IoModuleDto
{
    public string Module { get; set; } = string.Empty;

    public List<IoPointDto> Points { get; set; } = [];
}

/// <summary>
/// 一个点：装机信息（点表来的，不变）+ 当前值（每包刷新）。
/// </summary>
public class IoPointDto
{
    public int Index { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>归属部件（Door、Bowl1、Nozzle_DIW……）。</summary>
    public string Component { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>工程单位（AI/AO 用）。</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>是输出点（DO/AO）——界面上输出点才给强制/下发的入口。</summary>
    public bool IsOutput { get; set; }

    /// <summary>数字量当前状态；模拟量固定 false。</summary>
    public bool IsOn { get; set; }

    /// <summary>显示值：数字量是 0/1，模拟量是标定后的工程值。</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>这一包有没有读到；false 时上面的值是陈旧的，界面显示成"—"。</summary>
    public bool IsValid { get; set; }
}
