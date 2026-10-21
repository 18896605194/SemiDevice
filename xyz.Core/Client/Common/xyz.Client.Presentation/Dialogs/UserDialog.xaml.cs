using System.Collections;
using System.Windows;
using xyz.Client.Presentation.Localization;

namespace xyz.Client.Presentation.Dialogs;

/// <summary>
/// 新建 / 改账号框：一般经 DialogService.ShowUser 弹出
/// </summary>
public partial class UserDialog : Window
{
    private readonly bool _isNew;

    private readonly Func<UserForm, Task<string?>> _submit;

    /// <summary>
    /// roles：选角色的候选（要有 DisplayName、Description 两个属性）；canChangeRole = false 时角色不能改（内置 admin）；
    /// submit 拿填的去建 / 改，成功回 null，没成回给人看的原因（框不关）
    /// </summary>
    public UserDialog(string title, UserForm initial, bool isNew, bool canChangeRole, IEnumerable roles, Func<UserForm, Task<string?>> submit)
    {
        InitializeComponent();

        _isNew = isNew;
        _submit = submit;
        TitleText.Text = title;
        DisplayNameBox.Text = initial.DisplayName;
        RolePicker.ItemsSource = roles;
        RolePicker.Value = initial.RoleName;
        RolePicker.IsEnabled = canChangeRole;
        if (isNew)
        {
            UserNameBox.Text = initial.UserName;
            UserNameText.Visibility = Visibility.Collapsed;
            return;
        }

        // 改账号：登录名不能改、没有密码
        UserNameBox.Visibility = Visibility.Collapsed;
        UserNameText.Text = initial.UserName;
        PasswordPanel.Visibility = Visibility.Collapsed;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_isNew)
        {
            UserNameBox.Focus();
            return;
        }

        DisplayNameBox.Focus();
    }

    private async void OnOkClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(RolePicker.Value))
        {
            ErrorText.Text = L10n.Get("account.role.required");
            return;
        }

        if (_isNew && PasswordInput.Password != ConfirmPasswordInput.Password)
        {
            ErrorText.Text = L10n.Get("account.password.mismatch");
            ConfirmPasswordInput.Clear();
            ConfirmPasswordInput.Focus();
            return;
        }

        string userName = _isNew ? UserNameBox.Text.Trim() : UserNameText.Text;
        var form = new UserForm(userName, DisplayNameBox.Text.Trim(), RolePicker.Value, PasswordInput.Password);
        OkButton.IsEnabled = false;
        try
        {
            string? error = await _submit(form);
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
