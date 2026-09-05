using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace xyz.Client.Presentation.Dialogs;

/// <summary>
/// 通用“文本框 + 下拉框”输入弹窗。
/// </summary>
public partial class TextSelectDialog : Window
{
    private readonly bool _textRequired;
    private readonly bool _selectionRequired;

    public TextSelectDialog()
        : this("输入", "请输入内容", "请选择", Array.Empty<object>())
    {
    }

    public TextSelectDialog(
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
        InitializeComponent();

        _textRequired = textRequired;
        _selectionRequired = selectionRequired;

        Title = title;
        TextPromptText.Text = textPrompt;
        SelectPromptText.Text = selectPrompt;

        SelectComboBox.ItemsSource = itemsSource;
        SelectComboBox.DisplayMemberPath = displayMemberPath ?? string.Empty;
        SelectComboBox.SelectedItem = initialSelectedItem;
        InputTextBox.Text = initialText ?? string.Empty;

        UpdateOkButtonState();
    }

    public string Text => InputTextBox.Text.Trim();

    public object? SelectedItem => SelectComboBox.SelectedItem;

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateOkButtonState();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateOkButtonState();
    }

    private void UpdateOkButtonState()
    {
        var textOk = !_textRequired || !string.IsNullOrWhiteSpace(InputTextBox.Text);
        var selectionOk = !_selectionRequired || SelectComboBox.SelectedItem != null;
        OkButton.IsEnabled = textOk && selectionOk;
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
