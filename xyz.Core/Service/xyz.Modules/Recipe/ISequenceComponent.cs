using xyz.Components.Interfaces;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 流程配方库
/// </summary>
public interface ISequenceComponent : ISequenceLibrary
{
    /// <summary>
    /// 按名字取一个流程配方（副本），没有是 null
    /// </summary>
    SequenceData? Find(string name);

    /// <summary>
    /// 编号上限
    /// </summary>
    int Capacity { get; }

    /// <summary>
    /// 全部流程配方（副本），按编号从小到大
    /// </summary>
    IReadOnlyList<SequenceData> List();

    /// <summary>
    /// 按编号取一个
    /// </summary>
    HandleResult Get(int index);

    /// <summary>
    /// 可选的站点分组
    /// </summary>
    IReadOnlyList<SequenceStationGroup> StationGroups { get; }

    /// <summary>
    /// 保存（版本对不上不存）
    /// </summary>
    HandleResult Save(int index, int revision, string description, IReadOnlyList<SequenceStep> steps, string operatorName);

    /// <summary>
    /// 改名
    /// </summary>
    HandleResult Rename(int index, string name, string operatorName);

    /// <summary>
    /// 删除
    /// </summary>
    HandleResult Delete(int index, string operatorName);

    /// <summary>
    /// 在空编号上新建
    /// </summary>
    HandleResult Create(int index, string name, string operatorName);
}
