using Mapster;
using xyz.Components.Attributes;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Database.Jobs;
using xyz.Database.Wafers;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// 对象映射（Mapster）：一个类转成另一个类（账上的片 → 存盘行、流水行、人工调整记录这类）走这儿。
/// 同名字段、枚举转字符串 Mapster 自己会对；名字对不上的在构造里配一条，装配时就配好了，用的地方不用管谁先注册。
/// </summary>
[Component(description: "对象映射（Mapster）：字段名对不上的映射规则都配在这儿")]
public class MapperComponent : ComponentBase, IMapper
{
    /// <summary>
    /// 组件自己的一套规则，不放 Mapster 全局：别处直接 Adapt 的不受这里影响。
    /// </summary>
    private readonly TypeAdapterConfig _config = new();

    public MapperComponent()
    {

        // 账上的片 → 当前账存盘行：片的 Id 在表里叫 WaferGuid；走过的位置存成逗号隔开的一列
        _config.NewConfig<WaferInfo, WaferCurrentEntity>()
            .Map(entity => entity.WaferGuid, wafer => wafer.Id.ToString())
            .Map(entity => entity.Path, wafer => LedgerManagerComponent.PathText(wafer.Path));

        // 账上的片 → 流水行：表自己的 Id 是雪花号，不能拿片的 Id 填；发生时刻就是片最后变动的时刻。动作、从哪来由记流水的地方填
        _config.NewConfig<WaferInfo, WaferHistoryEntity>()
            .Ignore(entity => entity.Id)
            .Map(entity => entity.WaferGuid, wafer => wafer.Id.ToString())
            .Map(entity => entity.OccurredAt, wafer => wafer.UpdatedAt);

        // 账上的片 → 人工调整记录：表自己的 Id 同样不动。动作、从哪到哪、操作人、原因、时刻由记录的地方填
        _config.NewConfig<WaferInfo, WaferAdjustmentEntity>()
            .Ignore(entity => entity.Id)
            .Map(entity => entity.WaferGuid, wafer => wafer.Id.ToString());

        // 报警 → 给界面的报警：名字对不上的三项改一下；说明、处理办法空的转成空串；时间转本机时间（不写的话不一定按本机时间转）
        _config.NewConfig<AlarmItem, AlarmDto>()
            .Map(dto => dto.Source, item => item.SourcePath)
            .Map(dto => dto.Code, item => item.AlarmCode)
            .Map(dto => dto.Text, item => item.AlarmText)
            .Map(dto => dto.Description, item => item.Description ?? string.Empty)
            .Map(dto => dto.Solution, item => item.Solution ?? string.Empty)
            .Map(dto => dto.RaisedAt, item => item.RaisedAt.LocalDateTime)
            .Map(dto => dto.ClearedAt, item => LocalTimeOf(item.ClearedAt));

        // DI 信号 → 给界面的信号：名字、触发了没有（读不到是 null）
        _config.NewConfig<IDiSensor, DiSensorDto>()
            .Map(dto => dto.Name, signal => signal.Name)
            .Map(dto => dto.IsActive, signal => signal.IsActive);

        // 槽 → 给界面的槽：路径是全路径，液位、状态推枚举的数；五个信号按上一条转，没配的是 null
        _config.NewConfig<TankComponent, TankDto>()
            .Map(dto => dto.Path, tank => tank.FullPath)
            .Map(dto => dto.Level, tank => (int)tank.Level)
            .Map(dto => dto.State, tank => (int)tank.State);

        // CJ 记录 → 历史页的 CJ：建的时刻在表里叫 CreatedTime
        _config.NewConfig<ControlJobEntity, ControlJobHistoryDto>()
            .Map(dto => dto.CreatedAt, row => row.CreatedTime);

        // PJ 记录 → PJ：表的 Id 是行号、PJ 名在 Name；表里没 LotID 的给空串；每片的任务明细存的是 JSON
        _config.NewConfig<ProcessJobEntity, ProcessJobDto>()
            .Map(dto => dto.Id, row => row.Name)
            .Map(dto => dto.LotId, row => row.LotId ?? string.Empty)
            .Map(dto => dto.CreatedAt, row => row.CreatedTime)
            .Map(dto => dto.Wafers, row => WafersOf(row.Wafers));
    }

    /// <summary>
    /// PJ 记录里的任务明细 JSON 转回每片的任务，空的是没有片
    /// </summary>
    private static List<JobWaferDto> WafersOf(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        return JsonHelper.Deserialize<List<JobWaferDto>>(json) ?? [];
    }

    /// <summary>
    /// 清除时间转本机时间，没清除是 null
    /// </summary>
    private static DateTime? LocalTimeOf(DateTimeOffset? time)
    {
        if (time is null)
        {
            return null;
        }

        return time.Value.LocalDateTime;
    }

    /// <summary>
    /// 按这里的规则把 source 转成一个新的 TDestination。
    /// </summary>
    public TDestination Map<TDestination>(object source)
    {
        return source.Adapt<TDestination>(_config);
    }
}
