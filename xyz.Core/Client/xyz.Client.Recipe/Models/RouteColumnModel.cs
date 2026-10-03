namespace xyz.Client.Recipe.Models;

/// <summary>
/// 路线预览的一列 = 流程的一步：步号、这一步勾的站点（几个就叠几个框，多个的外面画虚线框表示任选）、工艺配方。
/// 按流程步骤自动生成，只是看的。
/// </summary>
public sealed class RouteColumnModel
{
    public RouteColumnModel(int number, IReadOnlyList<RouteBoxModel> boxes, string recipeText, bool hasRecipeError)
    {
        Number = number;
        Boxes = boxes;
        RecipeText = recipeText;
        HasRecipeError = hasRecipeError;
    }

    public int Number { get; }

    public IReadOnlyList<RouteBoxModel> Boxes { get; }

    /// <summary>
    /// 勾了不止一个：外面画虚线框（哪个空去哪个）。
    /// </summary>
    public bool IsMulti => Boxes.Count > 1;

    /// <summary>
    /// 框下面的字：工艺配方名，没选时是红字提示；不要配方的分组为空。
    /// </summary>
    public string RecipeText { get; }

    public bool HasRecipeError { get; }

    /// <summary>
    /// 前面画箭头（第一列不画）。
    /// </summary>
    public bool HasArrow => Number > 1;
}
