using xyz.Shared.Dtos;
using xyz.Elements.Attributes;

namespace xyz.Elements.Interfaces;


[Ioc]
public interface IRecipeLibrary
{
    /// <summary>库里全部工艺配方的名字，按编号从小到大。</summary>
    IReadOnlyList<string> RecipeNames { get; }

    /// <summary>
    /// 上报口
    /// </summary>
    IE30Callback? E30Callback { get; set; }

    /// <summary>取一个工艺配方的内容（JSON）；没有这个名字返回 null。名字不分大小写。</summary>
    string? GetRecipe(string name);

    /// <summary>这份 JSON 看样子是不是工艺配方（每一步是字段名 → 值）。Host 下一个库里还没有的名字时，靠它定存不存进这个库。</summary>
    bool AcceptsRecipe(string json);

    /// <summary>
    /// 按名字存：库里没有就在第一个空编号上新建，有就把说明和步骤整个换成 JSON 里的（版本加 1）。
    /// 名字、每一步的检查（按字段表、合计时长）跟本地保存一样，不过就回原因（错误码 + 参数），库里什么都不动。
    /// </summary>
    HandleResult ImportRecipe(string name, string json, string operatorName);

    /// <summary>按名字删；没有这个名字回原因。</summary>
    HandleResult DeleteRecipe(string name, string operatorName);
}
