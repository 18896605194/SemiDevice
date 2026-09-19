using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 当前报警的显示模型，由 <see cref="AlarmDto"/> 映射，字段同名对齐。顶栏报警栏、实时报警页共用。
/// 同一条报警（来源 + 代码）报着的时候不会再报，内容不变，所以不需要属性通知。
/// </summary>
public class AlarmModel
{
    /// <summary>报警来源的组件路径，如 LoadPort1、Robot1。</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>报警代码，同一来源内唯一。</summary>
    public string Code { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    /// <summary>分类（AlarmCategory 名字）。</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>等级：Warn / Alarm1 / Alarm2 / Fatal。</summary>
    public string Level { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>处理建议。</summary>
    public string Solution { get; init; } = string.Empty;

    public DateTime RaisedAt { get; init; }

    public static AlarmModel From(AlarmDto dto)
    {
        return new AlarmModel
        {
            Source = dto.Source,
            Code = dto.Code,
            Text = dto.Text,
            Category = dto.Category,
            Level = dto.Level,
            Description = dto.Description,
            Solution = dto.Solution,
            RaisedAt = dto.RaisedAt,
        };
    }
}
