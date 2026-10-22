using System.Windows;

namespace xyz.Client.Presentation.Dialogs;

/// <summary>
/// 登录框：一般经 DialogService.ShowLogin 弹出
/// </summary>
public partial class LoginDialog : Window
{
    private readonly Func<string, string, Task<string?>> _login;

    /// <summary>
    /// login：拿登录名、密码去登录，成功回 null，没成回给人看的原因（框不关）；notice：标题下面的黄字提醒，没有给 null
    /// </summary>
    public LoginDialog(string userName, bool showGuestHint, string? notice, Func<string, string, Task<string?>> login)
    {
        InitializeComponent();

        _login = login;
        UserNameBox.Text = userName;
        NoticeText.Text = notice ?? string.Empty;
        if (!showGuestHint)
        {
            GuestHint.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        // 登录名填好了（换人时是上一个人）直接输密码
        if (UserNameBox.Text.Length > 0)
        {
            PasswordInput.Focus();
            return;
        }

        UserNameBox.Focus();
    }

    private async void OnLoginClick(object sender, RoutedEventArgs e)
    {
        LoginButton.IsEnabled = false;
        ErrorText.Text = string.Empty;
        try
        {
            string? error = await _login(UserNameBox.Text.Trim(), PasswordInput.Password);
            if (error is null)
            {
                DialogResult = true;
                return;
            }

            ErrorText.Text = error;
            PasswordInput.Clear();
            PasswordInput.Focus();
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
