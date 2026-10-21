using System.Windows;

namespace xyz.Client.Presentation.Dialogs;

/// <summary>
/// 新建 / 改角色框：一般经 DialogService.ShowRole 弹出
/// </summary>
public partial class RoleDialog : Window
{
    private readonly Func<string, string, Task<string?>> _submit;

    /// <summary>
    /// submit 拿名字、说明去建 / 改，成功回 null，没成回给人看的原因（框不关）
    /// </summary>
    public RoleDialog(string title, string name, string description, Func<string, string, Task<string?>> submit)
    {
        InitializeComponent();

        _submit = submit;
        TitleText.Text = title;
        NameBox.Text = name;
        DescriptionBox.Text = description;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        NameBox.Focus();
    }

    private async void OnOkClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        OkButton.IsEnabled = false;
        try
        {
            string? error = await _submit(NameBox.Text.Trim(), DescriptionBox.Text.Trim());
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
