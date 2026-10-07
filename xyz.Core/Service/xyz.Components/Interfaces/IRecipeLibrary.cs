using xyz.Shared.Dtos;

namespace xyz.Components.Interfaces;

/// <summary>
/// 配方库给 EAP 的命令接口：Host 远程按名字列、取、存、删配方（SEMI E30 工艺程序管理，S7），跟本地配方页过同一套检查。
/// 流程配方库、工艺配方库都实现它（两个口子一模一样，所以共用一个接口）。
/// 配方内容是 JSON：库里那份配方原样转的，取出来什么样、存回去就什么样；名字、编号、版本号、建 / 改的人和时间以库为准，JSON 里带的不管。
/// 任意线程可调。
/// </summary>
public interface IRecipeLibrary
{
    /// <summary>库里全部配方的名字，按编号从小到大。</summary>
    IReadOnlyList<string> RecipeNames { get; }

    /// <summary>
    /// 上报口：配方建了、改了、删了（本地配方页、Host 改的都报；改名 = 旧名删了 + 新名建了）。没接 EAP 时为 null，库照常用。
    /// 报的时候放进 EAP 的派发组件，不占调用方的线程。
    /// </summary>
    IE30RecipeCallback? E30RecipeCallback { get; set; }

    /// <summary>取一个配方的内容（JSON）；没有这个名字返回 null。名字不分大小写。</summary>
    string? ExportRecipe(string name);

    /// <summary>
    /// 按名字存：库里没有就在第一个空编号上新建，有就把说明和步骤整个换成 JSON 里的（版本加 1）。
    /// 名字、步骤的检查跟本地保存一样，不过就回原因（错误码 + 参数），库里什么都不动。
    /// </summary>
    HandleResult ImportRecipe(string name, string json, string operatorName);

    /// <summary>按名字删；没有这个名字回原因。</summary>
    HandleResult DeleteRecipe(string name, string operatorName);
}
