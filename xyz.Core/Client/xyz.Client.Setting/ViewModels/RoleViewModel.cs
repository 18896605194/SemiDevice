using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using xyz.Client.Common.Events;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Modules;
using xyz.Client.Presentation.Dialogs;
using xyz.Client.Presentation.Localization;
using xyz.Client.Setting.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.Setting.ViewModels;

/// <summary>
/// 角色管理页：左边角色，右边这个角色每一项权限几档；权限项照代码里声明的菜单和功能项列，不进库
/// </summary>
public class RoleViewModel : BaseViewModel
{
    private const string LogModule = "Setting";

    #region Column

    /// <summary>
    /// 全部角色：管理员在前，访客在后
    /// </summary>
    public ObservableCollection<RoleModel> Roles { get; } = [];

    private RoleModel? _selectedRole;

    /// <summary>
    /// 选中的角色：换的时候有修改没保存先问要不要放弃
    /// </summary>
    public RoleModel? SelectedRole
    {
        get => _selectedRole;
        set
        {
            if (ReferenceEquals(_selectedRole, value))
            {
                return;
            }

            if (!_reloading && IsDirty && value is not null && !ConfirmDiscard())
            {
                // 不换：等列表这一下选中处理完再把选中拨回去
                Application.Current?.Dispatcher.InvokeAsync(() => OnPropertyChanged(nameof(SelectedRole)));
                return;
            }

            _selectedRole = value;
            OnPropertyChanged();
            RenameRoleCommand.NotifyCanExecuteChanged();
            DeleteRoleCommand.NotifyCanExecuteChanged();
            if (!_reloading)
            {
                _ = LoadPermissionsAsync();
            }
        }
    }

    /// <summary>
    /// 权限表：一级菜单、各项、功能项一行一行往下排
    /// </summary>
    public ObservableCollection<PermissionRowModel> Rows { get; } = [];

    private string _permissionTitle = string.Empty;

    /// <summary>
    /// 右边标题："权限 · 角色"
    /// </summary>
    public string PermissionTitle
    {
        get => _permissionTitle;
        private set => SetProperty(ref _permissionTitle, value);
    }

    private bool _isDirty;

    /// <summary>
    /// 有修改没保存
    /// </summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
            {
                SaveCommand.NotifyCanExecuteChanged();
                UndoCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _isLocked;

    /// <summary>
    /// 管理员：固定全部能操作，不能改
    /// </summary>
    public bool IsLocked
    {
        get => _isLocked;
        private set
        {
            if (SetProperty(ref _isLocked, value))
            {
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    #endregion

    #region Command

    public IAsyncRelayCommand CreateRoleCommand { get; }

    /// <summary>
    /// 改名和说明：内置的不能改
    /// </summary>
    public IAsyncRelayCommand RenameRoleCommand { get; }

    /// <summary>
    /// 删角色：内置的不能删，有账号在用的后端拒
    /// </summary>
    public IAsyncRelayCommand DeleteRoleCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    /// <summary>
    /// 撤销修改：回到存着的档位
    /// </summary>
    public IRelayCommand UndoCommand { get; }

    #endregion

    #region Service

    private readonly IRoleService _roleService;
    private readonly IReadOnlyList<IClientMenuProvider> _menuProviders;
    private IDisposable? _accountSubscription;

    /// <summary>
    /// 选中角色存着的档位（判断改没改、撤销用）
    /// </summary>
    private Dictionary<string, PermissionLevel> _saved = new(StringComparer.Ordinal);

    /// <summary>
    /// 重拉角色列表时换选中：不问、不重拉权限
    /// </summary>
    private bool _reloading;

    #endregion

    public RoleViewModel(IEnumerable<IClientMenuProvider> menuProviders)
    {
        _menuProviders = menuProviders.ToList();
        _roleService = GrpcClientFactory.Create<IRoleService>();

        CreateRoleCommand = new AsyncRelayCommand(DoCreateRole);
        RenameRoleCommand = new AsyncRelayCommand(DoRenameRole, CanEditRole);
        DeleteRoleCommand = new AsyncRelayCommand(DoDeleteRole, CanEditRole);
        SaveCommand = new AsyncRelayCommand(DoSave, () => IsDirty && !IsLocked && SelectedRole is not null);
        UndoCommand = new RelayCommand(DoUndo, () => IsDirty);
    }

    public override void Init()
    {
        BuildRows();

        _accountSubscription?.Dispose();
        _accountSubscription = RemoteEventSubscriber.Register<AccountChangedDto>(AccountChangedDto.EventToken, changed => _ = LoadRolesAsync(null));

        RemoteEventConnection.ConnectionChanged -= OnConnectionChanged;
        RemoteEventConnection.ConnectionChanged += OnConnectionChanged;
        OnConnectionChanged(RemoteEventConnection.IsConnected);
    }

    private void OnConnectionChanged(bool connected)
    {
        if (connected)
        {
            _ = LoadRolesAsync(null);
        }
    }

    #region 权限表

    /// <summary>
    /// 照声明的菜单、功能项排出权限表：一级菜单下有几项的先一行一级菜单再各项；按模块生成的几页合成一项；功能项排在它挂的菜单后面
    /// </summary>
    private void BuildRows()
    {
        foreach (var row in Rows)
        {
            row.LevelChanged -= OnLevelChanged;
        }

        Rows.Clear();
        var menus = _menuProviders
            .SelectMany(provider => provider.Menus)
            .DistinctBy(menu => menu.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var functions = _menuProviders
            .SelectMany(provider => provider.Functions)
            .DistinctBy(function => function.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var primary in menus.Where(menu => menu.ParentCode is null).OrderBy(menu => menu.Sort))
        {
            var children = menus
                .Where(menu => string.Equals(menu.ParentCode, primary.Code, StringComparison.OrdinalIgnoreCase))
                .OrderBy(menu => menu.Sort)
                .ToList();
            if (children.Count == 0 || children.All(child => string.Equals(child.PermissionCode, primary.PermissionCode, StringComparison.Ordinal)))
            {
                // 一级菜单本身就是一项：主界面；IO 下每个模块合成的就是它
                var access = children.Count == 0 ? primary.Access : children[0].Access;
                Rows.Add(CreateRow(primary.PermissionCode, NameOf(primary), access, PermissionRowKind.Item));
            }
            else
            {
                var childRows = children
                    .DistinctBy(child => child.PermissionCode, StringComparer.Ordinal)
                    .Select(child => CreateRow(child.PermissionCode, NameOf(child), child.Access, PermissionRowKind.Child))
                    .ToList();
                var settable = childRows.Where(row => !row.IsAdminOnly).ToList();
                bool canOperate = settable.Any(row => row.CanOperateLevel);
                string tip = L10n.Get(canOperate ? "permission.group_tip" : "permission.group_view_only_tip");
                var group = new PermissionRowModel(primary.Code, NameOf(primary), tip, PermissionRowKind.Group,
                    settable.Any(row => row.CanHide), canOperate);
                Rows.Add(group);
                foreach (var row in childRows)
                {
                    if (!row.IsAdminOnly)
                    {
                        row.Group = group;
                        group.Children.Add(row);
                    }

                    Rows.Add(row);
                }
            }

            foreach (var function in functions
                         .Where(function => string.Equals(function.ParentCode, primary.Code, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(function => function.Sort))
            {
                Rows.Add(new PermissionRowModel(function.Code, L10n.Get("permission." + function.Code), TipOf(function.Code),
                    PermissionRowKind.Function, false, true));
            }
        }

        foreach (var row in Rows)
        {
            row.LevelChanged += OnLevelChanged;
        }
    }

    private static PermissionRowModel CreateRow(string code, string name, MenuAccess access, PermissionRowKind kind)
    {
        switch (access)
        {
            case MenuAccess.AdminOnly:
                return new PermissionRowModel(code, name, TipOf(code), PermissionRowKind.AdminOnly, false, false);
            case MenuAccess.ViewOnly:
                return new PermissionRowModel(code, name, TipOf(code), kind, true, false);
            case MenuAccess.NoHide:
                return new PermissionRowModel(code, name, TipOf(code), kind, false, true);
            default:
                return new PermissionRowModel(code, name, TipOf(code), kind, true, true);
        }
    }

    /// <summary>
    /// 一项的叫法：合成的几页有自己的叫法（permission.{编码}），别的就是菜单名
    /// </summary>
    private static string NameOf(ClientMenu menu)
    {
        return L10n.Find("permission." + menu.PermissionCode) ?? menu.Title ?? L10n.MenuName(menu.Code);
    }

    /// <summary>
    /// 管哪些操作：permission.{编码}_tip，没配就空着
    /// </summary>
    private static string TipOf(string code)
    {
        return L10n.Find("permission." + code + "_tip") ?? string.Empty;
    }

    /// <summary>
    /// 把档位摆进表里：没配过的按"只看"；管理员全部能操作、锁上
    /// </summary>
    private void ApplyLevels(IReadOnlyDictionary<string, PermissionLevel> levels, bool locked)
    {
        foreach (var row in Rows)
        {
            row.IsEditable = !locked;
            if (row.IsGroup || row.IsAdminOnly)
            {
                continue;
            }

            PermissionLevel level;
            if (locked)
            {
                level = PermissionLevel.Operate;
            }
            else if (!levels.TryGetValue(row.Code, out level))
            {
                level = PermissionLevel.View;
            }

            row.Reset(level);
        }
    }

    private void OnLevelChanged()
    {
        IsDirty = Rows.Where(row => !row.IsGroup && !row.IsAdminOnly)
            .Any(row => row.Level != _saved.GetValueOrDefault(row.Code, PermissionLevel.View));
    }

    #endregion

    #region 拉数据

    /// <summary>
    /// 拉角色列表：选中 selectId 那个（没给就留在原来那个）；正在改着的不丢
    /// </summary>
    private async Task LoadRolesAsync(long? selectId)
    {
        try
        {
            var response = await _roleService.GetRolesAsync(new RpcRequest());
            if (!response.Success)
            {
                ClientLog.Error(LogModule, $"查角色失败：{ErrorText.Of(response)}");
                return;
            }

            long? keepId = selectId ?? SelectedRole?.Id;
            bool keepEditing = selectId is null && IsDirty;
            _reloading = true;
            try
            {
                Roles.Clear();
                foreach (var dto in response.DeserializeData<List<RoleDto>>())
                {
                    Roles.Add(ToModel(dto));
                }

                SelectedRole = Roles.FirstOrDefault(role => role.Id == keepId) ?? Roles.FirstOrDefault();
            }
            finally
            {
                _reloading = false;
            }

            if (!keepEditing || SelectedRole?.Id != keepId)
            {
                await LoadPermissionsAsync();
            }
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, $"查角色失败：{exception.Message}");
        }
    }

    private async Task LoadPermissionsAsync()
    {
        var role = SelectedRole;
        if (role is null)
        {
            PermissionTitle = string.Empty;
            return;
        }

        PermissionTitle = L10n.Get("setting.role.permissions", role.DisplayName);
        IsLocked = role.Kind == RoleKind.Admin;
        if (IsLocked)
        {
            _saved = new Dictionary<string, PermissionLevel>(StringComparer.Ordinal);
            ApplyLevels(_saved, true);
            IsDirty = false;
            return;
        }

        try
        {
            var response = await _roleService.GetPermissionsAsync(RpcRequest.Of((RpcParams.Id, role.Id)));
            if (!response.Success)
            {
                ClientLog.Error(LogModule, $"查角色 {role.DisplayName} 的权限失败：{ErrorText.Of(response)}");
                return;
            }

            // 拉的时候又换了角色：这一次的不要了
            if (!ReferenceEquals(role, SelectedRole))
            {
                return;
            }

            _saved = response.DeserializeData<RolePermissionsDto>().Items
                .ToDictionary(item => item.Code, item => item.Level, StringComparer.Ordinal);
            ApplyLevels(_saved, false);
            IsDirty = false;
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, $"查角色 {role.DisplayName} 的权限失败：{exception.Message}");
        }
    }

    private static RoleModel ToModel(RoleDto dto)
    {
        string description = dto.Description;
        string count = L10n.Get("setting.role.user_count", dto.UserCount);
        switch (dto.Kind)
        {
            case RoleKind.Admin:
                description = L10n.Get("setting.role.admin_description");
                break;
            case RoleKind.Guest:
                description = L10n.Get("setting.role.guest_description");
                count = L10n.Get("setting.role.guest_count");
                break;
            default:
                break;
        }

        return new RoleModel
        {
            Id = dto.Id,
            Name = dto.Name,
            DisplayName = RoleText.Of(dto.Kind, dto.Name),
            Description = description,
            Kind = dto.Kind,
            CountText = count
        };
    }

    #endregion

    #region 角色：建、改、删

    private bool CanEditRole()
    {
        var role = SelectedRole;
        return role is not null && !role.IsBuiltIn;
    }

    private async Task DoCreateRole()
    {
        long createdId = 0;
        bool created = DialogService.ShowRole(L10n.Get("account.role.create_title"), string.Empty, string.Empty, async (name, description) =>
        {
            var response = await _roleService.CreateRoleAsync(RpcRequest.Of((RpcParams.Name, name), (RpcParams.Description, description)));
            if (!response.Success)
            {
                return ErrorText.Of(response);
            }

            createdId = response.DeserializeData<RoleDto>().Id;
            return null;
        });
        if (!created)
        {
            return;
        }

        ClientLog.Info(LogModule, "新建了一个角色");
        await LoadRolesAsync(createdId);
    }

    private async Task DoRenameRole()
    {
        var role = SelectedRole;
        if (role is null || role.IsBuiltIn)
        {
            return;
        }

        bool renamed = DialogService.ShowRole(L10n.Get("account.role.edit_title"), role.Name, role.Description, async (name, description) =>
        {
            var response = await _roleService.UpdateRoleAsync(RpcRequest.Of(
                (RpcParams.Id, role.Id),
                (RpcParams.Name, name),
                (RpcParams.Description, description)));
            return response.Success ? null : ErrorText.Of(response);
        });
        if (renamed)
        {
            await LoadRolesAsync(role.Id);
        }
    }

    private async Task DoDeleteRole()
    {
        var role = SelectedRole;
        if (role is null || role.IsBuiltIn)
        {
            return;
        }

        if (!DialogService.ShowConfirm(L10n.Get("setting.role.delete_title"), L10n.Get("setting.role.delete_message", role.DisplayName),
                L10n.Get("common.delete"), true))
        {
            return;
        }

        try
        {
            var response = await _roleService.DeleteRoleAsync(RpcRequest.Of((RpcParams.Id, role.Id)));
            if (!response.Success)
            {
                ClientLog.Error(LogModule, $"删角色 {role.DisplayName} 没成：{ErrorText.Of(response)}");
                return;
            }

            ClientLog.Info(LogModule, $"删了角色 {role.DisplayName}");
            IsDirty = false;
            await LoadRolesAsync(0);
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, $"删角色 {role.DisplayName} 没成：{exception.Message}");
        }
    }

    #endregion

    #region 权限：存、撤销

    private async Task DoSave()
    {
        var role = SelectedRole;
        if (role is null || IsLocked)
        {
            return;
        }

        var items = Rows
            .Where(row => !row.IsGroup && !row.IsAdminOnly)
            .Select(row => new RolePermissionItem { Code = row.Code, Level = row.Level })
            .ToList();
        try
        {
            var response = await _roleService.SavePermissionsAsync(RpcRequest.Of((RpcParams.RoleId, role.Id), (RpcParams.Items, items)));
            if (!response.Success)
            {
                ClientLog.Error(LogModule, $"保存角色 {role.DisplayName} 的权限没成：{ErrorText.Of(response)}");
                return;
            }

            _saved = items.ToDictionary(item => item.Code, item => item.Level, StringComparer.Ordinal);
            IsDirty = false;
            ClientLog.Info(LogModule, $"角色 {role.DisplayName} 的权限已保存");
        }
        catch (Exception exception)
        {
            ClientLog.Error(LogModule, $"保存角色 {role.DisplayName} 的权限没成：{exception.Message}");
        }
    }

    private void DoUndo()
    {
        ApplyLevels(_saved, IsLocked);
        IsDirty = false;
    }

    /// <summary>
    /// 有修改没保存时换角色：问一下要不要放弃
    /// </summary>
    private bool ConfirmDiscard()
    {
        string name = SelectedRole?.DisplayName ?? string.Empty;
        bool discard = DialogService.ShowConfirm(L10n.Get("setting.role.discard_title"), L10n.Get("setting.role.discard_message", name),
            L10n.Get("setting.role.discard"), true);
        if (discard)
        {
            IsDirty = false;
        }

        return discard;
    }

    #endregion
}
