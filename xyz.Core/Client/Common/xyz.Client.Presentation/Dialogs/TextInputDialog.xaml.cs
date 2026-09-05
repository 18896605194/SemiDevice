using System.Windows;
using System.Windows.Controls;

namespace xyz.Client.Presentation.Dialogs;

/// <summary>
/// 通用单文本框输入弹窗。
/// </summary>
public partial class TextInputDialog : Window
{
    private readonly bool _required;

    public TextInputDialog()
        : this("输入", "请输入内容")
    {
    }

    public TextInputDialog(
        string title,
        string prompt,
        bool required = true,
        string? initialValue = null)
    {
        InitializeComponent();

        _required = required;
        Title = title;
        PromptText.Text = prompt;
        InputTextBox.Text = initialValue ?? string.Empty;
        OkButton.IsEnabled = !_required || !string.IsNullOrWhiteSpace(InputTextBox.Text);
    }

    public string Text => InputTextBox.Text.Trim();

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_required)
        {
            OkButton.IsEnabled = !string.IsNullOrWhiteSpace(InputTextBox.Text);
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
