namespace xyz.Modules;

/// <summary>
/// 供液模块给别的模块用的那一面：别的模块在 sc 上配它的模块名，开机按名字取到它，只经这里问
/// </summary>
public interface ISupplyAccess
{
    /// <summary>
    /// 模块名（sc 里配的就是它，如 IPA）
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 现在能不能供液：供液模块在自己扫描里算好，别的模块只读
    /// </summary>
    bool IsReady { get; }
}
