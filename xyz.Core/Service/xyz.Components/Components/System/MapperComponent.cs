using Mapster;
using xyz.Components.Attributes;
using xyz.Components.Models;
using xyz.Database.Wafers;

namespace xyz.Components.Components;

/// <summary>
/// 对象映射（Mapster）：一个类转成另一个类（账上的片 → 存盘行、流水行、人工调整记录这类）走这儿。
/// 同名字段、枚举转字符串 Mapster 自己会对；名字对不上的在构造里配一条，装配时就配好了，用的地方不用管谁先注册。
/// </summary>
[Component(description: "对象映射（Mapster）：字段名对不上的映射规则都配在这儿")]
public class MapperComponent : ComponentBase
{
    public static MapperComponent? Current { get; set; }

    /// <summary>
    /// 组件自己的一套规则，不放 Mapster 全局：别处直接 Adapt 的不受这里影响。
    /// </summary>
    private readonly TypeAdapterConfig _config = new();

    public MapperComponent()
    {
        Current = this;

        // 账上的片 → 当前账存盘行：片的 Id 在表里叫 WaferGuid
        _config.NewConfig<WaferInfo, WaferCurrentEntity>()
            .Map(entity => entity.WaferGuid, wafer => wafer.Id.ToString());

        // 账上的片 → 流水行：表自己的 Id 是雪花号，不能拿片的 Id 填；发生时刻就是片最后变动的时刻。动作、从哪来由记流水的地方填
        _config.NewConfig<WaferInfo, WaferHistoryEntity>()
            .Ignore(entity => entity.Id)
            .Map(entity => entity.WaferGuid, wafer => wafer.Id.ToString())
            .Map(entity => entity.OccurredAt, wafer => wafer.UpdatedAt);

        // 账上的片 → 人工调整记录：表自己的 Id 同样不动。动作、从哪到哪、操作人、原因、时刻由记录的地方填
        _config.NewConfig<WaferInfo, WaferAdjustmentEntity>()
            .Ignore(entity => entity.Id)
            .Map(entity => entity.WaferGuid, wafer => wafer.Id.ToString());
    }

    /// <summary>
    /// 按这里的规则把 source 转成一个新的 TDestination。
    /// </summary>
    public TDestination Map<TDestination>(object source)
    {
        return source.Adapt<TDestination>(_config);
    }
}
