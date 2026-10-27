namespace xyz.Elements.Interfaces;

/// <summary>
/// 对象映射：一个类转成另一个类
/// </summary>
public interface IMapper
{
    /// <summary>
    /// 按配好的规则把 source 转成一个新的 TDestination
    /// </summary>
    TDestination Map<TDestination>(object source);
}
