using xyz.Elements.Interfaces;
using xyz.Elements.Models;
using xyz.Shared.Dtos;
using xyz.Elements.Attributes;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 流程配方库
/// </summary>
[Ioc]
public interface ISequenceElement : ISequenceLibrary
{
    SequenceData? Find(string name);

    int Capacity { get; }

    IReadOnlyList<SequenceData> List();

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
