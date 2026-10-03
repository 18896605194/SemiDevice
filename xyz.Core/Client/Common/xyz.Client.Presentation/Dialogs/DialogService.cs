using System.Collections;
using System.Windows;
using xyz.Client.Presentation.Controls;

namespace xyz.Client.Presentation.Dialogs;

/// <summary>
/// 通用弹窗服务。
/// </summary>
public static class DialogService
{
    /// <summary>
    /// 弹出通用单文本框输入弹窗。
    /// 返回输入内容；取消返回 null。
    /// </summary>
    public static string? ShowTextInput(
        string title,
        string prompt,
        bool required = true,
        string? initialValue = null)
    {
        var dialog = new TextInputDialog(title, prompt, required, initialValue)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true ? dialog.Text : null;
    }

    /// <summary>
    /// 弹出“文本框 + 下拉框”输入弹窗。
    /// 返回输入文本和选中项；取消返回 null。
    /// </summary>
    public static TextSelectResult? ShowTextSelect(
        string title,
        string textPrompt,
        string selectPrompt,
        IEnumerable itemsSource,
        string? displayMemberPath = null,
        bool textRequired = true,
        bool selectionRequired = true,
        string? initialText = null,
        object? initialSelectedItem = null)
    {
        var dialog = new TextSelectDialog(
            title,
            textPrompt,
            selectPrompt,
            itemsSource,
            displayMemberPath,
            textRequired,
            selectionRequired,
            initialText,
            initialSelectedItem)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true
            ? new TextSelectResult(dialog.Text, dialog.SelectedItem)
            : null;
    }

    /// <summary>
    /// 弹出公共选择弹窗：标题、列、数据由调用方给，单选。返回选中的那一项；取消返回 null。
    /// 给了 currentValue、valuePath 就先选中值对得上的那一项（如选择框里现在填的）。
    /// </summary>
    public static object? ShowPicker(
        string title,
        IEnumerable<PickerColumn> columns,
        IEnumerable items,
        string? currentValue = null,
        string? valuePath = null)
    {
        var dialog = new PickerDialog(title, columns, items, currentValue, valuePath)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true ? dialog.SelectedItem : null;
    }
}

/// <summary>
/// 文本框 + 下拉框弹窗结果。
/// </summary>
public sealed record TextSelectResult(string Text, object? SelectedItem);
