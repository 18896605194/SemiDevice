using System.Windows;

namespace xyz.Client.Presentation.Dialogs;

/// <summary>
/// 公共确认弹窗：做之前让人看清楚要动什么（全部回片列出每一片从哪回哪、回不去的为什么）。
/// 一般经 DialogService.ShowConfirm 弹出。
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string okText, bool danger, bool showCancel)
    {
        InitializeComponent();

        TitleText.Text = title;
        MessageText.Text = message;
        OkButton.Content = okText;
        if (danger)
        {
            OkButton.Style = (Style)FindResource("ToolbarDangerButtonStyle");
        }

        if (!showCancel)
        {
            CancelButton.Visibility = Visibility.Collapsed;
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
