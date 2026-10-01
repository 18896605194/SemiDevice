using xyz.Configs.Models;
using xyz.Shared.Dtos;

namespace xyz.Service.Systems;

/// <summary>
/// EC 值节点转契约：EC 组件在组件层、不引用契约层，所以转换放在服务层。
/// gRPC 查询、改值回包和事件流推送共用这一份。
/// </summary>
internal static class EcMapper
{
    /// <summary>
    /// 组件全路径 + 值节点 → EcItemDto（键 = 组件全路径.参数名）。
    /// </summary>
    public static EcItemDto ToDto(this EcValueConfig value, string path)
    {
        return new EcItemDto
        {
            Key = $"{path}.{value.Name}",
            Format = value.Format,
            Min = value.Min,
            Max = value.Max,
            Unit = value.Unit,
            Default = value.Default,
            Value = value.Value,
            Description = value.Description,
            Options = value.Options,
        };
    }
}
