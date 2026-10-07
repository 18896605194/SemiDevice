using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 流程配方库给 EAP 的命令接口：Host 远程按名字列、取、存、删流程配方（SEMI E30 工艺程序管理，S7），跟本地流程配方页过同一套检查。
/// 内容是 JSON：库里那份流程配方原样转的，取出来什么样、存回去就什么样；名字、编号、版本号、建 / 改的人和时间以库为准，JSON 里带的不管。
/// 任意线程可调。
/// </summary>
public interface ISequenceComponent
{
    /// <summary>库里全部流程配方的名字，按编号从小到大。</summary>
    IReadOnlyList<string> SequenceNames { get; }

    /// <summary>
    /// 上报口：流程配方建了、改了、删了（本地流程配方页、Host 改的都报；改名 = 旧名删了 + 新名建了）。没接 EAP 时为 null，库照常用。
    /// 报的时候放进 EAP 的派发组件，不占调用方的线程。
    /// </summary>
    IE30Callback? E30Callback { get; set; }

    /// <summary>取一个流程配方的内容（JSON）；没有这个名字返回 null。名字不分大小写。</summary>
    string? ExportSequence(string name);

    /// <summary>这份 JSON 看样子是不是流程配方（每一步是分组 + 站点）。Host 下一个库里还没有的名字时，靠它定存不存进这个库。</summary>
    bool AcceptsSequence(string json);

    /// <summary>
    /// 按名字存：库里没有就在第一个空编号上新建，有就把说明和步骤整个换成 JSON 里的（版本加 1）。
    /// 名字、步骤的检查跟本地保存一样，不过就回原因（错误码 + 参数），库里什么都不动。
    /// </summary>
    HandleResult ImportSequence(string name, string json, string operatorName);

    /// <summary>按名字删；没有这个名字回原因。</summary>
    HandleResult DeleteSequence(string name, string operatorName);
}
