using System.Windows;
using xyz.Client.Presentation.Localization;

namespace xyz.Client.Presentation.Dialogs;

/// <summary>
/// 改密码 / 重置密码框：一般经 DialogService.ShowPassword 弹出
/// </summary>
public partial class PasswordDialog : Window
{
    private readonly bool _needOld;

    private readonly Func<string, string, Task<string?>> _submit;

    /// <summary>
    /// needOld：要不要旧密码（改自己的要、管理员重置不要）；submit 拿旧密码、新密码去改，成功回 null，没成回给人看的原因（框不关）
    /// </summary>
    public PasswordDialog(string title, string subtitle, bool needOld, Func<string, string, Task<string?>> submit)
    {
        InitializeComponent();

        _needOld = needOld;
        _submit = submit;
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        if (!needOld)
        {
            OldPasswordPanel.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_needOld)
        {
            OldPasswordInput.Focus();
            return;
        }

        NewPasswordInput.Focus();
    }

    private async void OnOkClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (NewPasswordInput.Password != ConfirmPasswordInput.Password)
        {
            ErrorText.Text = L10n.Get("account.password.mismatch");
            ConfirmPasswordInput.Clear();
            ConfirmPasswordInput.Focus();
            return;
        }

        OkButton.IsEnabled = false;
        try
        {
            string? error = await _submit(OldPasswordInput.Password, NewPasswordInput.Password);
            if (error is null)
            {
                DialogResult = true;
                return;
            }

            ErrorText.Text = error;
        }
        finally
        {
            OkButton.IsEnabled = true;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
