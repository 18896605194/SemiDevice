using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using xyz.Client.Common.Events;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Dialogs;
using xyz.Client.Presentation.Localization;
using xyz.Client.Setting.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.Setting.ViewModels;

/// <summary>
/// 用户管理页：账号表 + 新建、改、重置密码、停用 / 启用、删除（只有管理员进得来）
/// </summary>
public class UserViewModel : BaseViewModel
{
    private const string LogModule = "Setting";

    /// <summary>
    /// 最后登录时间的写法
    /// </summary>
    private const string TimeFormat = "yyyy-MM-dd HH:mm";

    #region Column

    public ObservableCollection<UserModel> Users { get; } = [];

    private UserModel? _selectedUser;

    public UserModel? SelectedUser
    {
        get => _selectedUser;
        set
        {
            if (SetProperty(ref _selectedUser, value))
            {
                EditUserCommand.NotifyCanExecuteChanged();
                ResetPasswordCommand.NotifyCanExecuteChanged();
                ToggleEnabledCommand.NotifyCanExecuteChanged();
                DeleteUserCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(ToggleText));
            }
        }
    }

    private string _countText = string.Empty;

    /// <summary>
    /// 标题条上的"几个账号"
    /// </summary>
    public string CountText
    {
        get => _countText;
        private set => SetProperty(ref _countText, value);
    }

    /// <summary>
    /// 停用 / 启用按钮的字：选中的停用了就是"启用"
    /// </summary>
    public string ToggleText => L10n.Get(SelectedUser is not null && !SelectedUser.IsEnabled ? "setting.user.enable" : "setting.user.disable");

    #endregion

    #region Command

    public IAsyncRelayCommand CreateUserCommand { get; }

    /// <summary>
    /// 改显示名、角色
    /// </summary>
    public IAsyncRelayCommand EditUserCommand { get; }

    /// <summary>
    /// 重置密码：不要旧密码
    /// </summary>
    public IAsyncRelayCommand ResetPasswordCommand { get; }

    /// <summary>
    /// 停用 / 启用：内置账号不能停用
    /// </summary>
    public IAsyncRelayCommand ToggleEnabledCommand { get; }

    /// <summary>
    /// 删账号：内置账号不能删
    /// </summary>
    public IAsyncRelayCommand DeleteUserCommand { get; }

    #endregion

    #region Service

    private readonly IUserService _userService;
    private readonly IRoleService _roleService;
    private IDisposable? _accountSubscription;

    /// <summary>
    /// 能给账号用的角色（访客不给）：新建、改账号时选
    /// </summary>
    private List<RoleModel> _roles = [];

    #endregion

    public UserViewModel()
    {
        _userService = GrpcClientFactory.Create<IUserService>();
        _roleService = GrpcClientFactory.Create<IRoleService>();

        CreateUserCommand = new AsyncRelayCommand(DoCreateUser);
        EditUserCommand = new AsyncRelayCommand(DoEditUser, () => SelectedUser is not null);
        ResetPasswordCommand = new AsyncRelayCommand(DoResetPassword, () => SelectedUser is not null);
        ToggleEnabledCommand = new AsyncRelayCommand(DoToggleEnabled, CanChangeSelected);
        DeleteUserCommand = new AsyncRelayCommand(DoDeleteUser, CanChangeSelected);
    }

    public override void Init()
    {
        _accountSubscription?.Dispose();
        _accountSubscription = EventBus.Register<AccountChangedDto>(AccountChangedDto.EventToken, changed => _ = LoadAsync());

        RemoteEventBus.ConnectionChanged -= OnConnectionChanged;
        RemoteEventBus.ConnectionChanged += OnConnectionChanged;
        OnConnectionChanged(RemoteEventBus.IsConnected);
    }

    private void OnConnectionChanged(bool connected)
    {
        if (connected)
        {
            _ = LoadAsync();
        }
    }

    private bool CanChangeSelected()
    {
        var user = SelectedUser;
        return user is not null && !user.IsBuiltIn;
    }

    #region 拉数据

    /// <summary>
    /// 拉角色（选角色的候选）和账号，留在原来选中的那个
    /// </summary>
    private async Task LoadAsync()
    {
        try
        {
            var roleResponse = await _roleService.GetRolesAsync(new RpcRequest());
            if (roleResponse.Success)
            {
                _roles = roleResponse.DeserializeData<List<RoleDto>>()
                    .Where(role => role.Kind != RoleKind.Guest)
                    .Select(role => new RoleModel
                    {
                        Id = role.Id,
                        Name = role.Name,
                        DisplayName = RoleText.Of(role.Kind, role.Name),
                        Description = role.Kind == RoleKind.Admin ? L10n.Get("setting.role.admin_description") : role.Description,
                        Kind = role.Kind
                    })
                    .ToList();
            }

            var response = await _userService.GetUsersAsync(new RpcRequest());
            if (!response.Success)
            {
                ClientLog.Error(LogModule, $"查账号失败：{ErrorText.Of(response)}");
                return;
            }

            long? keepId = SelectedUser?.Id;
            Users.Clear();
            foreach (var dto in response.DeserializeData<List<UserDto>>())
            {
                Users.Add(ToModel(dto));
            }

            CountText = L10n.Get("setting.user.count", Users.Count);
            SelectedUser = Users.FirstOrDefault(user => user.Id == keepId) ?? Users.FirstOrDefault();
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, $"查账号失败：{exception.Message}");
        }
    }

    private static UserModel ToModel(UserDto dto)
    {
        string note = string.Empty;
        if (dto.IsBuiltIn)
        {
            note = L10n.Get("setting.user.builtin_note");
        }
        else if (!dto.IsEnabled)
        {
            note = L10n.Get("setting.user.disabled_note");
        }

        return new UserModel
        {
            Id = dto.Id,
            UserName = dto.UserName,
            DisplayName = dto.DisplayName,
            RoleId = dto.RoleId,
            RoleKind = dto.RoleKind,
            RoleName = dto.RoleName,
            RoleText = RoleText.Of(dto.RoleKind, dto.RoleName),
            IsEnabled = dto.IsEnabled,
            IsBuiltIn = dto.IsBuiltIn,
            StatusText = L10n.Get(dto.IsEnabled ? "setting.user.enabled" : "setting.user.disabled"),
            LastLoginText = dto.LastLoginTime?.ToString(TimeFormat) ?? string.Empty,
            Note = note
        };
    }

    #endregion

    #region 建、改、重置密码、停用、删

    private async Task DoCreateUser()
    {
        var empty = new UserForm(string.Empty, string.Empty, string.Empty, string.Empty);
        bool created = DialogService.ShowUser(L10n.Get("account.user.create_title"), empty, true, true, _roles, async form =>
        {
            var role = RoleOf(form.RoleName);
            if (role is null)
            {
                return L10n.Get("account.role.required");
            }

            var response = await _userService.CreateUserAsync(RpcRequest.Of(
                (RpcParams.UserName, form.UserName),
                (RpcParams.DisplayName, form.DisplayName),
                (RpcParams.RoleId, role.Id),
                (RpcParams.Password, form.Password)));
            return response.Success ? null : ErrorText.Of(response);
        });
        if (created)
        {
            ClientLog.Info(LogModule, "新建了一个账号");
            await LoadAsync();
        }
    }

    private async Task DoEditUser()
    {
        var user = SelectedUser;
        if (user is null)
        {
            return;
        }

        var initial = new UserForm(user.UserName, user.DisplayName, user.RoleText, string.Empty);
        bool edited = DialogService.ShowUser(L10n.Get("account.user.edit_title"), initial, false, !user.IsBuiltIn, _roles, async form =>
        {
            var role = RoleOf(form.RoleName);
            if (role is null)
            {
                return L10n.Get("account.role.required");
            }

            var response = await _userService.UpdateUserAsync(RpcRequest.Of(
                (RpcParams.Id, user.Id),
                (RpcParams.DisplayName, form.DisplayName),
                (RpcParams.RoleId, role.Id)));
            return response.Success ? null : ErrorText.Of(response);
        });
        if (edited)
        {
            ClientLog.Info(LogModule, $"改了账号 {user.UserName}");
            await LoadAsync();
        }
    }

    private Task DoResetPassword()
    {
        var user = SelectedUser;
        if (user is null)
        {
            return Task.CompletedTask;
        }

        bool reset = DialogService.ShowPassword(L10n.Get("account.password.reset_title"), L10n.Get("account.password.reset_subtitle", user.UserName),
            false, async (_, password) =>
            {
                var response = await _userService.ResetPasswordAsync(RpcRequest.Of((RpcParams.Id, user.Id), (RpcParams.Password, password)));
                return response.Success ? null : ErrorText.Of(response);
            });
        if (reset)
        {
            ClientLog.Info(LogModule, $"重置了账号 {user.UserName} 的密码");
        }

        return Task.CompletedTask;
    }

    private async Task DoToggleEnabled()
    {
        var user = SelectedUser;
        if (user is null || user.IsBuiltIn)
        {
            return;
        }

        try
        {
            var response = await _userService.SetEnabledAsync(RpcRequest.Of((RpcParams.Id, user.Id), (RpcParams.IsEnabled, !user.IsEnabled)));
            if (!response.Success)
            {
                ClientLog.Error(LogModule, $"停用 / 启用账号 {user.UserName} 没成：{ErrorText.Of(response)}");
                return;
            }

            ClientLog.Info(LogModule, user.IsEnabled ? $"停用了账号 {user.UserName}" : $"启用了账号 {user.UserName}");
            await LoadAsync();
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, $"停用 / 启用账号 {user.UserName} 没成：{exception.Message}");
        }
    }

    private async Task DoDeleteUser()
    {
        var user = SelectedUser;
        if (user is null || user.IsBuiltIn)
        {
            return;
        }

        if (!DialogService.ShowConfirm(L10n.Get("setting.user.delete_title"), L10n.Get("setting.user.delete_message", user.UserName),
                L10n.Get("common.delete"), true))
        {
            return;
        }

        try
        {
            var response = await _userService.DeleteUserAsync(RpcRequest.Of((RpcParams.Id, user.Id)));
            if (!response.Success)
            {
                ClientLog.Error(LogModule, $"删账号 {user.UserName} 没成：{ErrorText.Of(response)}");
                return;
            }

            ClientLog.Info(LogModule, $"删了账号 {user.UserName}");
            await LoadAsync();
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, $"删账号 {user.UserName} 没成：{exception.Message}");
        }
    }

    /// <summary>
    /// 选角色框填回的是显示名：找回是哪个角色
    /// </summary>
    private RoleModel? RoleOf(string displayName)
    {
        return _roles.FirstOrDefault(role => string.Equals(role.DisplayName, displayName, StringComparison.Ordinal));
    }

    #endregion
}
