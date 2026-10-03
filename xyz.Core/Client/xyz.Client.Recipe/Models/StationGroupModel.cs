using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;

namespace xyz.Client.Recipe.Models;

/// <summary>
/// 一个可选的站点分组（来自 sc.xml 的分组节点和下面装的模块），名字原样显示；"添加"时在公共选择弹窗里列出来挑。
/// </summary>
public sealed class StationGroupModel
{
    public StationGroupModel(SequenceStationGroupDto dto)
    {
        Name = dto.Name;
        Modules = dto.Modules;
        IsLoadPort = dto.IsLoadPort;
        NeedsRecipe = dto.NeedsRecipe;
    }

    public string Name { get; }

    public IReadOnlyList<string> Modules { get; }

    /// <summary>
    /// 弹窗"模块"那一列：模块名连起来（分隔符跟着语言走，弹窗每次打开现取）。
    /// </summary>
    public string ModulesText => string.Join(L10n.Get("recipe.sequence.list_separator"), Modules);

    public bool IsLoadPort { get; }

    public bool NeedsRecipe { get; }
}
