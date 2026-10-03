namespace xyz.Client.Recipe.Models;

/// <summary>
/// 选工艺配方弹窗里的一行：编号、名称、说明。
/// </summary>
public sealed class RecipeOptionModel
{
    public RecipeOptionModel(string no, string name, string description)
    {
        No = no;
        Name = name;
        Description = description;
    }

    public string No { get; }

    public string Name { get; }

    public string Description { get; }
}
