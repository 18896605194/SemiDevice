using xyz.Components.Enums;
using xyz.Configs.Models;
using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// EC 参数：读写、落盘
/// </summary>
public interface IEcComponent
{
    /// <summary>
    /// 值变了（界面改的、组件自己写的都算）
    /// </summary>
    event Action<string, EcValueConfig>? ValueChanged;

    /// <summary>
    /// 读一个 EC 值；没有这一项是空串
    /// </summary>
    string Get(string path, string name);

    /// <summary>
    /// 改一个 EC 值并落盘；值没变返回 false
    /// </summary>
    bool Set(string path, string name, string value);

    /// <summary>
    /// 只查能不能改成这个值，不改
    /// </summary>
    EcSetResult Check(string path, string name, string value);

    /// <summary>
    /// 查过再改，带回改后的那一项
    /// </summary>
    EcSetResult TrySet(string path, string name, string value, out EcValueConfig? item);

    /// <summary>
    /// 全部 EC 项的快照
    /// </summary>
    IReadOnlyList<(string Path, EcValueConfig Value)> Snapshot();

    /// <summary>
    /// 开机把组件树上的 EC 定义和 ec.xml 合并
    /// </summary>
    bool Merge(IEnumerable<ComponentBase> roots);

    /// <summary>
    /// 一项 EC 转成给界面的样子
    /// </summary>
    EcItemDto CreateDto(string path, EcValueConfig value);
}
