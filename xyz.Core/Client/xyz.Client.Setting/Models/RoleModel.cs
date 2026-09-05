using CommunityToolkit.Mvvm.ComponentModel;

namespace xyz.Client.Setting.Models;

/// <summary>
/// 用户角色。
/// </summary>
public class RoleModel : ObservableObject
{
    private string _name = string.Empty;
    private string _description = string.Empty;
    private int _userCount;

    public long Id { get; set; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public int UserCount
    {
        get => _userCount;
        set => SetProperty(ref _userCount, value);
    }
}
