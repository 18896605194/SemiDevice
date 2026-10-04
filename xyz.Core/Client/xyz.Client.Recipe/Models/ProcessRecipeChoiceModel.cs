namespace xyz.Client.Recipe.Models;

/// <summary>
/// 工艺步骤里下拉框的一项：值和显示的字（不选那一项值是空的、显示"—"）。下拉框按 Value 选中（SelectedValuePath = Value），显示 Text。
/// </summary>
public sealed class ProcessRecipeChoiceModel
{
    public ProcessRecipeChoiceModel(string value, string text)
    {
        Value = value;
        Text = text;
    }

    public string Value { get; }

    public string Text { get; }
}
