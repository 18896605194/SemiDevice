namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 公共选择弹窗的一列：表头（语言包 key）、取数据项的哪个属性、列宽（不写 = 占满剩下的宽度）。
/// 选择框（PickerBox）在 XAML 里写，页面代码直接调 DialogService.ShowPicker 时在代码里建。
/// </summary>
public sealed class PickerColumn
{
    /// <summary>
    /// 表头文字的语言包 key（如 common.name），弹出时按当前语言取。
    /// </summary>
    public string HeaderKey { get; set; } = string.Empty;

    /// <summary>
    /// 数据项的属性名（如 Name）。
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// 列宽；不写（NaN）就占满剩下的宽度。
    /// </summary>
    public double Width { get; set; } = double.NaN;
}
