using CommunityToolkit.Mvvm.ComponentModel;

namespace xyz.Client.Setting.Models;

/// <summary>
/// 用户模型。
/// </summary>
public class UserModel : ObservableObject
{
    private string _userName = string.Empty;
    private string _displayName = string.Empty;
    private string _roleName = string.Empty;
    private bool _isEnabled = true;

    public long Id { get; set; }

    public string UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string RoleName
    {
        get => _roleName;
        set => SetProperty(ref _roleName, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }
}
