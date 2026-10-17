using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 流程配方库 接口，同时也给eap使用
/// </summary>
public interface ISequenceLibrary
{
    /// <summary>
    /// 库里全部流程配方的名字，按编号从小到大
    /// </summary>
    IReadOnlyList<string> SequenceNames { get; }

    /// <summary>
    /// 上报口
    /// </summary>
    IE30Callback? E30Callback { get; set; }

    /// <summary>
    /// 取一个流程配方的内容
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    string? GetSequenceData(string name);

    /// <summary>
    /// 判定是不是流程配方
    /// </summary>
    /// <param name="json"></param>
    /// <returns></returns>
    bool AcceptsSequence(string json);

    /// <summary>
    /// 按名字存：库里没有就在第一个空编号上新建，有就把说明和步骤整个换成 JSON 里的（版本加 1）。
    /// 名字、步骤的检查跟本地保存一样，不过就回原因（错误码 + 参数），库里什么都不动。
    /// </summary>
    HandleResult ImportSequence(string name, string json, string operatorName);

    /// <summary>
    /// 按名字删
    /// </summary>
    /// <param name="name"></param>
    /// <param name="operatorName"></param>
    /// <returns></returns>
    HandleResult DeleteSequence(string name, string operatorName);
}
