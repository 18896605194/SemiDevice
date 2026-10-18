using xyz.Components.Interfaces;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 工艺配方库
/// </summary>
public interface IRecipeComponent : IRecipeLibrary
{
    /// <summary>
    /// 按名字取一个工艺配方（副本），没有是 null
    /// </summary>
    RecipeData? Find(string name);

    /// <summary>
    /// 步骤的字段表
    /// </summary>
    IReadOnlyList<RecipeField> Fields { get; }

    /// <summary>
    /// 配方头的字段表
    /// </summary>
    IReadOnlyList<RecipeField> HeadFields { get; }

    /// <summary>
    /// 库里有没有这个名字
    /// </summary>
    bool Contains(string name);

    /// <summary>
    /// 这个配方跟这个站点对不对得上，对不上带回是哪一项
    /// </summary>
    (string Field, string Value)? FindMismatch(string recipeName, string station);

    /// <summary>
    /// 编号上限
    /// </summary>
    int Capacity { get; }

    /// <summary>
    /// 全部工艺配方（副本），按编号从小到大
    /// </summary>
    IReadOnlyList<RecipeData> List();

    /// <summary>
    /// 按编号取一个
    /// </summary>
    HandleResult Get(int index);

    /// <summary>
    /// 能做工艺的站点配了的几种 Type
    /// </summary>
    IReadOnlyList<string> StationTypes { get; }

    /// <summary>
    /// 保存（版本对不上不存）
    /// </summary>
    HandleResult Save(int index, int revision, RecipeData content, string operatorName);

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
