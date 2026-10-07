using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 工艺配方库给 EAP 的命令接口：Host 远程按名字列、取、存、删工艺配方（SEMI E30 工艺程序管理，S7），跟本地工艺配方页过同一套检查。
/// 内容是 JSON：库里那份工艺配方原样转的（每一步是字段名 → 值），取出来什么样、存回去就什么样；名字、编号、版本号、建 / 改的人和时间以库为准，JSON 里带的不管。
/// 任意线程可调。
/// </summary>
public interface IProcessRecipeComponent
{
    /// <summary>库里全部工艺配方的名字，按编号从小到大。</summary>
    IReadOnlyList<string> ProcessRecipeNames { get; }

    /// <summary>
    /// 上报口：工艺配方建了、改了、删了（本地工艺配方页、Host 改的都报；改名 = 旧名删了 + 新名建了）。没接 EAP 时为 null，库照常用。
    /// 报的时候放进 EAP 的派发组件，不占调用方的线程。
    /// </summary>
    IE30Callback? E30Callback { get; set; }

    /// <summary>取一个工艺配方的内容（JSON）；没有这个名字返回 null。名字不分大小写。</summary>
    string? ExportProcessRecipe(string name);

    /// <summary>这份 JSON 看样子是不是工艺配方（每一步是字段名 → 值）。Host 下一个库里还没有的名字时，靠它定存不存进这个库。</summary>
    bool AcceptsProcessRecipe(string json);

    /// <summary>
    /// 按名字存：库里没有就在第一个空编号上新建，有就把说明和步骤整个换成 JSON 里的（版本加 1）。
    /// 名字、每一步的检查（按字段表、合计时长）跟本地保存一样，不过就回原因（错误码 + 参数），库里什么都不动。
    /// </summary>
    HandleResult ImportProcessRecipe(string name, string json, string operatorName);

    /// <summary>按名字删；没有这个名字回原因。</summary>
    HandleResult DeleteProcessRecipe(string name, string operatorName);
}
