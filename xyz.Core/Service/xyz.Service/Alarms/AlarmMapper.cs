using xyz.Components.Alarm;
using xyz.Database.Alarms;
using xyz.Shared.Dtos;

namespace xyz.Service.Alarms;

/// <summary>
/// 报警对象转契约：报警组件、报警记录表都在后端层，契约层不认识它们，所以转换放在服务层。
/// 事件流推送和 gRPC 查询共用这一份。
/// </summary>
internal static class AlarmMapper
{
    /// <summary>
    /// 运行时报警快照 → AlarmDto（时间转本机时间）。
    /// </summary>
    public static AlarmDto ToDto(this AlarmItem item)
    {
        return new AlarmDto
        {
            Source = item.SourcePath,
            Code = item.AlarmCode,
            Text = item.AlarmText,
            Category = item.Category.ToString(),
            Level = item.Level.ToString(),
            Description = item.Description ?? string.Empty,
            Solution = item.Solution ?? string.Empty,
            RaisedAt = item.RaisedAt.LocalDateTime,
            ClearedAt = item.ClearedAt?.LocalDateTime,
        };
    }

    /// <summary>
    /// 报警记录行 → AlarmHistoryDto。
    /// </summary>
    public static AlarmHistoryDto ToDto(this AlarmHistoryEntity entity)
    {
        return new AlarmHistoryDto
        {
            Action = entity.Action,
            Source = entity.Source,
            Code = entity.Code,
            Text = entity.Text,
            Category = entity.Category,
            Level = entity.Level,
            RaisedAt = entity.RaisedAt,
            OccurredAt = entity.OccurredAt,
        };
    }
}
