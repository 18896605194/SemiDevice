namespace xyz.Client.DataCenter.Models;

/// <summary>
/// 实时报警页的一行，由 AlarmDto 经 Mapster 映射，字段与 DTO 同名对齐。
/// 同一条报警（来源 + 代码）报着的时候不会再报，行内容不变，所以不需要属性通知。
/// </summary>
public class AlarmModel
{
    /// <summary>报警来源的组件路径，如 LoadPort1、Robot1。</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>报警代码，同一来源内唯一。</summary>
    public string Code { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    /// <summary>分类（AlarmCategory 名字）。</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>等级：Warn / Alarm1 / Alarm2 / Fatal。</summary>
    public string Level { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>处理建议。</summary>
    public string Solution { get; set; } = string.Empty;

    public DateTime RaisedAt { get; set; }
}
