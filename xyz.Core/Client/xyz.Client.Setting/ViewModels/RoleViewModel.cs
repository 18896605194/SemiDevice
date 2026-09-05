using CommunityToolkit.Mvvm.Input;
using Mapster;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Dialogs;
using xyz.Client.DataModels.Rpc;
using xyz.Client.Setting.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Services;

namespace xyz.Client.Setting.ViewModels;

/// <summary>
/// 角色管理页面 ViewModel。
/// </summary>
public class RoleViewModel : BaseViewModel
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    #region Column

    public ObservableCollection<RoleModel> Roles { get; }

    private RoleModel? _selectedRole;

    public RoleModel? SelectedRole
    {
        get => _selectedRole;
        set => SetProperty(ref _selectedRole, value);
    }

    #endregion

    #region Command

    public IAsyncRelayCommand CreateRoleCommand { get; }

    public IAsyncRelayCommand DeleteRoleCommand { get; }

    #endregion

    #region Service

    private readonly IRoleService _roleService;

    #endregion

    public RoleViewModel()
    {
        Roles = new ObservableCollection<RoleModel>();
        _roleService = GrpcClientFactory.Create<IRoleService>();

        CreateRoleCommand = new AsyncRelayCommand(DoCreateRole);
        DeleteRoleCommand = new AsyncRelayCommand(DoDeleteRole, CanDeleteRole);
    }

    public override void Init()
    {
        Roles.Clear();

        var response = _roleService
            .GetRolesAsync(new RpcRequest())
            .GetAwaiter()
            .GetResult();

        var roles = Deserialize<List<RoleDto>>(response);
        foreach (var role in roles)
        {
            Roles.Add(role.Adapt<RoleModel>());
        }

        SelectedRole = Roles.FirstOrDefault();
    }

    private bool CanDeleteRole()
    {
        return SelectedRole != null;
    }

    private async Task DoCreateRole()
    {
        var roleName = DialogService.ShowTextInput("创建角色", "角色名称");
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return;
        }

        var existsResponse = await _roleService.ExistsAsync(new RpcRequest
        {
            Parameters = new Dictionary<string, string>
            {
                ["Name"] = roleName
            }
        });

        if (Deserialize<bool>(existsResponse))
        {
            MessageBox.Show(
                "角色名称已存在，请更换后重试",
                "创建角色",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var createResponse = await _roleService.CreateRoleAsync(new RpcRequest
        {
            Parameters = new Dictionary<string, string>
            {
                ["Name"] = roleName
            }
        });
        var roleDto = Deserialize<RoleDto>(createResponse);
        var role = roleDto.Adapt<RoleModel>();

        Roles.Add(role);
        SelectedRole = role;
    }

    private async Task DoDeleteRole()
    {
        if (SelectedRole == null)
        {
            return;
        }

        var response = await _roleService.DeleteRoleAsync(new RpcRequest
        {
            Parameters = new Dictionary<string, string>
            {
                ["Id"] = SelectedRole.Id.ToString()
            }
        });
        EnsureSuccess(response);

        Roles.Remove(SelectedRole);
        SelectedRole = Roles.FirstOrDefault();
    }

    private static T Deserialize<T>(RpcResponse response)
    {
        EnsureSuccess(response);
        return JsonSerializer.Deserialize<T>(response.Data, JsonOptions)!;
    }

    private static void EnsureSuccess(RpcResponse response)
    {
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Message);
        }
    }
}
