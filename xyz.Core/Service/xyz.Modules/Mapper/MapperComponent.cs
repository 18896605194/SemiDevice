using Mapster;
using xyz.Common;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Database.Jobs;
using xyz.Database.Wafers;
using xyz.Shared.Dtos;
using xyz.Tools;

namespace xyz.Modules;

/// <summary>
/// 对象映射（Mapster）
/// </summary>
[Component(description: "对象映射（Mapster）：字段名对不上的映射规则都配在这儿；项目继承它加自己的规则")]
public class MapperComponent : ComponentBase, IMapper
{
    /// <summary>
    /// 组件自己的一套规则，不放 Mapster 全局：别处直接 Adapt 的不受这里影响。
    /// </summary>
    private readonly TypeAdapterConfig _config = new();

    public MapperComponent()
    {
        AddRules(_config);
    }

    /// <summary>
    /// 配映射规则
    /// </summary>
    protected virtual void AddRules(TypeAdapterConfig config)
    {
        // 账上的片 → 当前账存盘行：片的 Id 在表里叫 WaferGuid；走过的位置存成逗号隔开的一列
        config.NewConfig<WaferInfo, WaferCurrentEntity>()
            .Map(entity => entity.WaferGuid, wafer => wafer.Id.ToString())
            .Map(entity => entity.Path, wafer => LedgerManagerComponent.PathText(wafer.Path));

        // 账上的片 → 流水行：表自己的 Id 是雪花号，不能拿片的 Id 填；发生时刻就是片最后变动的时刻。动作、从哪来由记流水的地方填
        config.NewConfig<WaferInfo, WaferHistoryEntity>()
            .Ignore(entity => entity.Id)
            .Map(entity => entity.WaferGuid, wafer => wafer.Id.ToString())
            .Map(entity => entity.OccurredAt, wafer => wafer.UpdatedAt);

        // 账上的片 → 人工调整记录：表自己的 Id 同样不动。动作、从哪到哪、操作人、原因、时刻由记录的地方填
        config.NewConfig<WaferInfo, WaferAdjustmentEntity>()
            .Ignore(entity => entity.Id)
            .Map(entity => entity.WaferGuid, wafer => wafer.Id.ToString());

        // 人工调整记录 → 账单调整页的历史：发生时刻在界面叫 Time
        config.NewConfig<WaferAdjustmentEntity, WaferAdjustmentDto>()
            .Map(dto => dto.Time, row => row.OccurredAt);

        // 报警 → 给界面的报警：名字对不上的三项改一下；说明、处理办法空的转成空串；时间转本机时间（不写的话不一定按本机时间转）
        config.NewConfig<AlarmItem, AlarmDto>()
            .Map(dto => dto.Source, item => item.SourcePath)
            .Map(dto => dto.Code, item => item.AlarmCode)
            .Map(dto => dto.Text, item => item.AlarmText)
            .Map(dto => dto.Description, item => item.Description ?? string.Empty)
            .Map(dto => dto.Solution, item => item.Solution ?? string.Empty)
            .Map(dto => dto.RaisedAt, item => item.RaisedAt.LocalDateTime)
            .Map(dto => dto.ClearedAt, item => DateTimeHelper.LocalTimeOf(item.ClearedAt));

        // DI 信号 → 给界面的信号：名字、触发了没有（读不到是 null）
        config.NewConfig<IDiSensor, DiSensorDto>()
            .Map(dto => dto.Name, signal => signal.Name)
            .Map(dto => dto.IsActive, signal => signal.IsActive);

        // 槽 → 给界面的槽：路径是全路径，液位、状态推枚举的数；五个信号按上一条转，没配的是 null。
        // 按接口拿到的槽（模块下面的 Tanks）实际是哪个类就照哪个类的规则转
        config.NewConfig<TankComponent, TankDto>()
            .Map(dto => dto.Path, tank => tank.FullPath)
            .Map(dto => dto.Level, tank => (int)tank.Level)
            .Map(dto => dto.State, tank => (int)tank.State);
        config.NewConfig<ITank, TankDto>()
            .Include<TankComponent, TankDto>();

        // CJ 记录 → 历史页的 CJ：建的时刻在表里叫 CreatedTime
        config.NewConfig<ControlJobEntity, ControlJobHistoryDto>()
            .Map(dto => dto.CreatedAt, row => row.CreatedTime);

        // PJ 记录 → PJ：表的 Id 是行号、PJ 名在 Name；表里没 LotID 的给空串；每片的任务明细存的是 JSON
        config.NewConfig<ProcessJobEntity, ProcessJobDto>()
            .Map(dto => dto.Id, row => row.Name)
            .Map(dto => dto.LotId, row => row.LotId ?? string.Empty)
            .Map(dto => dto.CreatedAt, row => row.CreatedTime)
            .Map(dto => dto.Wafers, row => WafersOf(row.Wafers));

        // 工艺配方 → 给界面：配方头自己加的项、每一步的值存的是"名字 + 值"的列表，界面要字典（同名的取先出现的）
        config.NewConfig<RecipeData, ProcessRecipeDto>()
            .Map(dto => dto.Head, data => ValuesOf(data.Head));
        config.NewConfig<RecipeStep, ProcessRecipeStepDto>()
            .Map(dto => dto.Values, step => ValuesOf(step));

        // 工艺配方的字段 → 给界面：下拉选项按站点 Type 分开的那一套照抄；上限要看当前晶圆尺寸、配方头的选项要看腔体配了哪些 Type，由服务转完再补
        config.NewConfig<RecipeField, ProcessRecipeFieldDto>()
            .Map(dto => dto.OptionsByType, field => field.OptionsByType.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()));
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
    /// 配方一步（或配方头）的值转成字典，同名的取先出现的
    /// </summary>
    private static Dictionary<string, string> ValuesOf(RecipeStep? step)
    {
        var values = new Dictionary<string, string>();
        if (step is null)
        {
            return values;
        }

        foreach (var value in step.Values)
        {
            values.TryAdd(value.Name, value.Value);
        }

        return values;
    }

    /// <summary>
    /// 按这里的规则把 source 转成一个新的 TDestination。
    /// </summary>
    public TDestination Map<TDestination>(object source)
    {
        return source.Adapt<TDestination>(_config);
    }
}
