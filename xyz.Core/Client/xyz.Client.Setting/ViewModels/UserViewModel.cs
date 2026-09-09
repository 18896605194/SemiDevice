using CommunityToolkit.Mvvm.Input;
using Mapster;
using System.Collections.ObjectModel;
using xyz.Shared.Rpc;
using System.Windows;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Dialogs;
using xyz.Client.DataModels.Rpc;
using xyz.Client.Setting.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Services;

namespace xyz.Client.Setting.ViewModels;

/// <summary>
/// 用户管理页面 ViewModel。
/// </summary>
public class UserViewModel : BaseViewModel
{
    #region Column

    public ObservableCollection<UserModel> Users { get; }

    public ObservableCollection<RoleModel> Roles { get; }

    private UserModel? _selectedUser;

    public UserModel? SelectedUser
    {
        get => _selectedUser;
        set => SetProperty(ref _selectedUser, value);
    }

    #endregion

    #region Command

    public IAsyncRelayCommand CreateUserCommand { get; }

    public IAsyncRelayCommand DeleteUserCommand { get; }

    #endregion

    #region Service

    private readonly IUserService _userService;
    private readonly IRoleService _roleService;

    #endregion

    public UserViewModel()
    {
        Users = new ObservableCollection<UserModel>();
        Roles = new ObservableCollection<RoleModel>();

        _userService = GrpcClientFactory.Create<IUserService>();
        _roleService = GrpcClientFactory.Create<IRoleService>();

        CreateUserCommand = new AsyncRelayCommand(DoCreateUser);
        DeleteUserCommand = new AsyncRelayCommand(DoDeleteUser, CanDeleteUser);
    }

    public override void Init()
    {
        Users.Clear();
        Roles.Clear();

        var roleResponse = _roleService
            .GetRolesAsync(new RpcRequest())
            .GetAwaiter()
            .GetResult();

        foreach (var roleDto in roleResponse.DeserializeData<List<RoleDto>>())
        {
            Roles.Add(roleDto.Adapt<RoleModel>());
        }

        var userResponse = _userService
            .GetUsersAsync(new RpcRequest())
            .GetAwaiter()
            .GetResult();

        foreach (var userDto in userResponse.DeserializeData<List<UserDto>>())
        {
            Users.Add(userDto.Adapt<UserModel>());
        }

        SelectedUser = Users.FirstOrDefault();
    }

    private bool CanDeleteUser()
    {
        return SelectedUser != null;
    }

    private async Task DoCreateUser()
    {
        var result = DialogService.ShowTextSelect(
            "创建用户",
            "用户名",
            "所属角色",
            Roles,
            nameof(RoleModel.Name));

        if (result == null)
        {
            return;
        }

        var userName = result.Text;
        var role = (RoleModel)result.SelectedItem!;

        var existsResponse = await _userService.ExistsAsync(new RpcRequest
        {
            Parameters = new Dictionary<string, string>
            {
                ["UserName"] = userName
            }
        });

        if (existsResponse.DeserializeData<bool>())
        {
            MessageBox.Show(
                "用户名已存在，请更换后重试",
                "创建用户",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var createResponse = await _userService.CreateUserAsync(new RpcRequest
        {
            Parameters = new Dictionary<string, string>
            {
                ["UserName"] = userName,
                ["DisplayName"] = userName,
                ["RoleName"] = role.Name
            }
        });

        var userDto = createResponse.DeserializeData<UserDto>();
        var user = userDto.Adapt<UserModel>();

        Users.Add(user);
        SelectedUser = user;
    }

    private async Task DoDeleteUser()
    {
        if (SelectedUser == null)
        {
            return;
        }

        var response = await _userService.DeleteUserAsync(new RpcRequest
        {
            Parameters = new Dictionary<string, string>
            {
                ["Id"] = SelectedUser.Id.ToString()
            }
        });
        response.EnsureSuccess();

        Users.Remove(SelectedUser);
        SelectedUser = Users.FirstOrDefault();
    }

}
