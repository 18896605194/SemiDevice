namespace xyz.Client.Recipe.Models;

/// <summary>
/// 工艺步骤里下拉框的一项（摆臂、方式）：值和显示的字。下拉框按 Value 选中（SelectedValuePath = Value），显示 Text。
/// </summary>
public sealed class ProcessRecipeChoiceModel
{
    public ProcessRecipeChoiceModel(object value, string text)
    {
        Value = value;
        Text = text;
    }

    public object Value { get; }

    public string Text { get; }
}
