using System.Globalization;
using System.Security.Cryptography;
using SqlSugar;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Interfaces;
using xyz.Configs.Models;
using xyz.Database.Auth;
using xyz.Database.DbProvider;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Permissions;
using xyz.Tools;

namespace xyz.Components.Components;

/// <summary>
/// 账号组件：用户、角色和各菜单的权限
/// </summary>
[Component(description: "账号：用户、角色和每个角色对各菜单的权限（隐藏 / 只看 / 能操作）；IsEnable 开着客户端要登录")]
public class AccountComponent : ComponentBase, IAccount
{
    #region 常量

    /// <summary>
    /// 内置管理员的登录名
    /// </summary>
    public const string AdminUserName = "admin";

    /// <summary>
    /// 内置管理员第一次建出来时的密码
    /// </summary>
    public const string AdminInitialPassword = "admin";

    /// <summary>
    /// 登录名最长几个字（库里这一列的长度）
    /// </summary>
    private const int UserNameMaxLength = 50;

    /// <summary>
    /// 显示名最长几个字（库里这一列的长度）
    /// </summary>
    private const int DisplayNameMaxLength = 100;

    /// <summary>
    /// 角色名最长几个字（库里这一列的长度）
    /// </summary>
    private const int RoleNameMaxLength = 50;

    /// <summary>
    /// 角色说明最长几个字（库里这一列的长度）
    /// </summary>
    private const int DescriptionMaxLength = 500;

    /// <summary>
    /// 权限项编码最长几个字（库里这一列的长度）
    /// </summary>
    private const int CodeMaxLength = 100;

    /// <summary>
    /// 密码哈希的算法名，存在哈希串最前面
    /// </summary>
    private const string HashScheme = "PBKDF2-SHA256";

    /// <summary>
    /// 密码哈希的迭代次数
    /// </summary>
    private const int HashIterations = 100000;

    /// <summary>
    /// 盐几个字节
    /// </summary>
    private const int SaltBytes = 16;

    /// <summary>
    /// 哈希几个字节
    /// </summary>
    private const int HashBytes = 32;

    /// <summary>
    /// 哈希串各段的分隔：算法名$迭代次数$盐$哈希
    /// </summary>
    private const char HashSeparator = '$';

    /// <summary>
    /// 哈希串有几段
    /// </summary>
    private const int HashParts = 4;

    /// <summary>
    /// 老的账号、菜单表：换成 account_* 三张表、菜单写进代码以前，老的用户、角色、菜单服务建在默认库里的，没人用了
    /// </summary>
    private static readonly string[] LegacyTables = ["User", "Role", "Menu"];

    /// <summary>
    /// 闲置自动退出最长配多少分钟（一天）
    /// </summary>
    private const int IdleLogoutMaxMinutes = 1440;

    /// <summary>
    /// 密码最短几位能配的范围
    /// </summary>
    private const int PasswordMinLengthLowest = 1;

    private const int PasswordMinLengthHighest = 32;

    #endregion

    #region SC

    [SCEditor("True", "Account", "要不要登录：True = 客户端启动弹登录框，没登录按访客的权限；False = 不登录、不分权限，谁都能操作")]
    public bool IsEnable { get; set; } = true;

    [SCEditor("Default", "Account", "账号存在哪个库（sc.xml 的 Database 节点名）")]
    public string Database { get; set; } = "Default";

    /// <summary>
    /// 放 sc 不放 EC（用户 2026-10-22 定的）：EC 页工程师就能改，账号安全的规矩不该谁都能改
    /// </summary>
    [SCEditor("15", "Account", "闲置自动退出：客户端键盘鼠标多少分钟没动就退出登录、弹登录框（取消按访客）；0 = 不自动退出，最长 1440")]
    public int IdleLogoutMinutes { get; set; } = 15;

    [SCEditor("4", "Account", "密码最短几位（1 ~ 32）")]
    public int PasswordMinLength { get; set; } = 4;

    #endregion

    private readonly object _gate = new();

    /// <summary>
    /// 开机建表、预置做成了；没做成时登录、用户、角色都回账号库出错
    /// </summary>
    private bool _ready;

    #region 开机：建表、预置

    /// <summary>
    /// 闲置分钟数、密码最短几位配得不对开机就报
    /// </summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);
        if (IdleLogoutMinutes < 0 || IdleLogoutMinutes > IdleLogoutMaxMinutes)
        {
            throw new InvalidOperationException(
                $"sc.xml 节点 {setting.Name} 的 IdleLogoutMinutes={IdleLogoutMinutes} 不对：0 ~ {IdleLogoutMaxMinutes}（0 = 不自动退出）。");
        }

        if (PasswordMinLength < PasswordMinLengthLowest || PasswordMinLength > PasswordMinLengthHighest)
        {
            throw new InvalidOperationException(
                $"sc.xml 节点 {setting.Name} 的 PasswordMinLength={PasswordMinLength} 不对：{PasswordMinLengthLowest} ~ {PasswordMinLengthHighest}。");
        }
    }

    /// <summary>
    /// 建三张表、删老表；内置的管理员、访客角色和 admin 账号没有就建，第一次开机再预置工程师、操作员两个角色
    /// </summary>
    public override bool InitComponent()
    {
        bool succeeded = base.InitComponent();
        try
        {
            lock (_gate)
            {
                using var db = CreateDb();
                db.CodeFirst.InitTables<UserEntity, RoleEntity, RolePermissionEntity>();
                DropLegacyTables(db);

                // 事务里出的错 SqlSugar 自己接住、回滚，看结果
                var seeded = db.Ado.UseTran(() => Seed(db));
                if (!seeded.IsSuccess)
                {
                    LogHelper.Error(FullPath, $"预置账号、角色失败，登录、用户、角色都用不了：{seeded.ErrorMessage}");
                    return false;
                }

                _ready = true;
            }
        }
        catch (Exception exception)
        {
            LogHelper.Error(FullPath, $"账号库建表失败，登录、用户、角色都用不了：{exception.Message}");
            return false;
        }

        return succeeded;
    }

    /// <summary>
    /// 库里还留着老表的删掉（开机看见才删，删过就没有了）；删不掉不耽误登录，下次开机再删
    /// </summary>
    private void DropLegacyTables(SqlSugarClient db)
    {
        foreach (string table in LegacyTables)
        {
            try
            {
                if (db.DbMaintenance.IsAnyTable(table, false))
                {
                    db.DbMaintenance.DropTable(table);
                    LogHelper.Info(FullPath, $"删掉了不再用的老表 {table}（账号已经换成 account_* 三张表，菜单写在代码里）");
                }
            }
            catch (Exception exception)
            {
                LogHelper.Warn(FullPath, $"老表 {table} 没删掉（不影响登录），下次开机再删：{exception.Message}");
            }
        }
    }

    private void Seed(SqlSugarClient db)
    {
        var roles = db.Queryable<RoleEntity>().ToList();
        bool firstTime = roles.Count == 0;

        #region 内置角色：管理员、访客

        var admin = roles.FirstOrDefault(role => role.Kind == (int)RoleKind.Admin);
        if (admin is null)
        {
            admin = InsertRole(db, "管理员", "全部能操作，固定不能改", RoleKind.Admin, null);
        }

        if (!roles.Any(role => role.Kind == (int)RoleKind.Guest))
        {
            InsertRole(db, "访客", "没登录的人按它算", RoleKind.Guest,
                Defaults(PermissionLevel.View, PermissionLevel.View, PermissionLevel.Hidden, PermissionLevel.Hidden,
                    PermissionLevel.View, PermissionLevel.Hidden, PermissionLevel.Hidden));
        }

        #endregion

        #region 第一次开机预置：工程师、操作员

        if (firstTime)
        {
            InsertRole(db, "工程师", "跑货、调机、改配方、IO", RoleKind.Normal,
                Defaults(PermissionLevel.Operate, PermissionLevel.Operate, PermissionLevel.Operate, PermissionLevel.Operate,
                    PermissionLevel.Operate, PermissionLevel.Operate, PermissionLevel.Operate));
            InsertRole(db, "操作员", "跑货、复位报警，其余只看", RoleKind.Normal,
                Defaults(PermissionLevel.Operate, PermissionLevel.Operate, PermissionLevel.View, PermissionLevel.View,
                    PermissionLevel.Operate, PermissionLevel.Hidden, PermissionLevel.Hidden));
            LogHelper.Info(FullPath, "第一次开机：预置了管理员、访客、工程师、操作员四个角色");
        }

        #endregion

        #region 内置账号 admin

        if (!db.Queryable<UserEntity>().Any(user => user.IsBuiltIn))
        {
            db.Insertable(new UserEntity
            {
                UserName = AdminUserName,
                DisplayName = AdminUserName,
                PasswordHash = HashPassword(AdminInitialPassword),
                RoleId = admin.Id,
                IsBuiltIn = true
            }).ExecuteCommand();
            LogHelper.Warn(FullPath, $"建了内置账号 {AdminUserName}，初始密码是 {AdminInitialPassword}，登录后请改掉");
        }

        #endregion
    }

    private static RoleEntity InsertRole(SqlSugarClient db, string name, string description, RoleKind kind,
        IReadOnlyDictionary<string, PermissionLevel>? permissions)
    {
        var role = new RoleEntity
        {
            Name = name,
            Description = description,
            Kind = (int)kind
        };
        role.Id = db.Insertable(role).ExecuteReturnBigIdentity();
        if (permissions is not null)
        {
            var rows = permissions.Select(item => new RolePermissionEntity
            {
                RoleId = role.Id,
                Code = item.Key,
                Level = (int)item.Value
            }).ToList();
            db.Insertable(rows).ExecuteCommand();
        }

        return role;
    }

    /// <summary>
    /// 预置角色的档位：报警、数据中心几页本来就只能看，其余按给的档位
    /// </summary>
    private static Dictionary<string, PermissionLevel> Defaults(PermissionLevel main, PermissionLevel system,
        PermissionLevel manual, PermissionLevel recipe, PermissionLevel alarmReset, PermissionLevel setting, PermissionLevel io)
    {
        return new Dictionary<string, PermissionLevel>(StringComparer.Ordinal)
        {
            [PermissionCodes.Main] = main,
            [PermissionCodes.MainSystem] = system,
            [PermissionCodes.ManualLoadPorts] = manual,
            [PermissionCodes.ManualRobot] = manual,
            [PermissionCodes.ManualChamber] = manual,
            [PermissionCodes.RecipeSequence] = recipe,
            [PermissionCodes.RecipeProcess] = recipe,
            [PermissionCodes.AlarmRealtime] = PermissionLevel.View,
            [PermissionCodes.AlarmHistory] = PermissionLevel.View,
            [PermissionCodes.AlarmReset] = alarmReset,
            [PermissionCodes.DataCenterLogRealtime] = PermissionLevel.View,
            [PermissionCodes.DataCenterLogHistory] = PermissionLevel.View,
            [PermissionCodes.DataCenterDataChart] = PermissionLevel.View,
            [PermissionCodes.DataCenterRealChart] = PermissionLevel.View,
            [PermissionCodes.DataCenterJobHistory] = PermissionLevel.View,
            [PermissionCodes.SettingEc] = setting,
            [PermissionCodes.SettingWaferLedger] = setting,
            [PermissionCodes.Io] = io,
        };
    }

    #endregion

    #region 登录、会话

    public HandleResult<SessionDto> Login(string userName, string password)
    {
        string name = (userName ?? string.Empty).Trim();
        lock (_gate)
        {
            if (!_ready)
            {
                return NotReady<SessionDto>();
            }

            try
            {
                using var db = CreateDb();
                var user = FindUser(db, name);
                if (user is null || !VerifyPassword(password ?? string.Empty, user.PasswordHash))
                {
                    LogHelper.Warn(FullPath, $"登录没成：{name}（登录名或密码不对）");
                    return HandleResult<SessionDto>.Fail(ErrorCodes.AccountLoginFailed);
                }

                if (!user.IsEnabled)
                {
                    LogHelper.Warn(FullPath, $"登录没成：{user.UserName} 已停用");
                    return HandleResult<SessionDto>.Fail(ErrorCodes.AccountUserDisabled, user.UserName);
                }

                var session = BuildSession(db, user);
                if (!session.IsSuccess)
                {
                    return session;
                }

                session.Result!.IdleLogoutMinutes = IdleLogoutMinutes;
                user.LastLoginTime = DateTime.Now;
                db.Updateable(user).UpdateColumns(item => new { item.LastLoginTime }).ExecuteCommand();
                LogHelper.Info(FullPath, $"{user.UserName} 登录（{session.Result?.RoleName}）");
                return session;
            }
            catch (Exception exception)
            {
                return Failed<SessionDto>("登录", exception);
            }
        }
    }

    public HandleResult<SessionDto> GetSession(string userName)
    {
        string name = (userName ?? string.Empty).Trim();
        lock (_gate)
        {
            if (!_ready)
            {
                return NotReady<SessionDto>();
            }

            try
            {
                using var db = CreateDb();
                if (name.Length == 0)
                {
                    var guest = db.Queryable<RoleEntity>().First(role => role.Kind == (int)RoleKind.Guest);
                    if (guest is null)
                    {
                        return HandleResult<SessionDto>.Fail(ErrorCodes.AccountRoleNotFound, RoleKind.Guest.ToString());
                    }

                    var guestSession = ToSession(db, guest, string.Empty, string.Empty);
                    guestSession.IdleLogoutMinutes = IdleLogoutMinutes;
                    return HandleResult<SessionDto>.Success(guestSession);
                }

                var user = FindUser(db, name);
                if (user is null)
                {
                    return HandleResult<SessionDto>.Fail(ErrorCodes.AccountUserNotFound, name);
                }

                if (!user.IsEnabled)
                {
                    return HandleResult<SessionDto>.Fail(ErrorCodes.AccountUserDisabled, user.UserName);
                }

                var session = BuildSession(db, user);
                if (session.IsSuccess)
                {
                    session.Result!.IdleLogoutMinutes = IdleLogoutMinutes;
                }

                return session;
            }
            catch (Exception exception)
            {
                return Failed<SessionDto>("取会话", exception);
            }
        }
    }

    public void NoteLogout(string userName, bool isIdle)
    {
        string name = (userName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return;
        }

        if (isIdle)
        {
            LogHelper.Info(FullPath, $"{name} 闲置自动注销");
            return;
        }

        LogHelper.Info(FullPath, $"{name} 注销");
    }

    public HandleResult ChangePassword(string userName, string oldPassword, string newPassword)
    {
        string name = (userName ?? string.Empty).Trim();
        lock (_gate)
        {
            if (!_ready)
            {
                return NotReady();
            }

            try
            {
                using var db = CreateDb();
                var user = FindUser(db, name);
                if (user is null)
                {
                    return HandleResult.Fail(ErrorCodes.AccountUserNotFound, name);
                }

                if (!VerifyPassword(oldPassword ?? string.Empty, user.PasswordHash))
                {
                    return HandleResult.Fail(ErrorCodes.AccountPasswordWrong);
                }

                var rejected = CheckPassword(newPassword);
                if (rejected is not null)
                {
                    return rejected;
                }

                user.PasswordHash = HashPassword(newPassword);
                user.UpdatedTime = DateTime.Now;
                db.Updateable(user).UpdateColumns(item => new { item.PasswordHash, item.UpdatedTime }).ExecuteCommand();
                LogHelper.Info(FullPath, $"{user.UserName} 改了自己的密码");
                return HandleResult.Success();
            }
            catch (Exception exception)
            {
                return Failed("改密码", exception);
            }
        }
    }

    /// <summary>
    /// 按账号的角色组会话；角色找不到（不该出现：有账号在用的角色删不掉）回角色不存在
    /// </summary>
    private static HandleResult<SessionDto> BuildSession(SqlSugarClient db, UserEntity user)
    {
        var role = db.Queryable<RoleEntity>().InSingle(user.RoleId);
        if (role is null)
        {
            return HandleResult<SessionDto>.Fail(ErrorCodes.AccountRoleNotFound, user.RoleId.ToString(CultureInfo.InvariantCulture));
        }

        string displayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName;
        return HandleResult<SessionDto>.Success(ToSession(db, role, user.UserName, displayName));
    }

    private static SessionDto ToSession(SqlSugarClient db, RoleEntity role, string userName, string displayName)
    {
        var session = new SessionDto
        {
            UserName = userName,
            DisplayName = displayName,
            RoleName = role.Name,
            RoleKind = (RoleKind)role.Kind
        };

        // 管理员全部能操作，不带档位
        if (role.Kind == (int)RoleKind.Admin)
        {
            return session;
        }

        foreach (var row in db.Queryable<RolePermissionEntity>().Where(item => item.RoleId == role.Id).ToList())
        {
            session.Permissions[row.Code] = (PermissionLevel)row.Level;
        }

        return session;
    }

    #endregion

    #region 账号

    public HandleResult<IReadOnlyList<UserDto>> GetUsers()
    {
        lock (_gate)
        {
            if (!_ready)
            {
                return NotReady<IReadOnlyList<UserDto>>();
            }

            try
            {
                using var db = CreateDb();
                var roles = db.Queryable<RoleEntity>().ToList().ToDictionary(role => role.Id);
                IReadOnlyList<UserDto> users = db.Queryable<UserEntity>()
                    .OrderBy(user => user.Id)
                    .ToList()
                    .Select(user => ToUserDto(user, roles))
                    .ToList();
                return HandleResult<IReadOnlyList<UserDto>>.Success(users);
            }
            catch (Exception exception)
            {
                return Failed<IReadOnlyList<UserDto>>("查账号", exception);
            }
        }
    }

    public HandleResult<UserDto> CreateUser(UserCreateRequest request)
    {
        string name = (request.UserName ?? string.Empty).Trim();
        string displayName = (request.DisplayName ?? string.Empty).Trim();
        HandleResult<UserDto> result;
        lock (_gate)
        {
            if (!_ready)
            {
                return NotReady<UserDto>();
            }

            try
            {
                using var db = CreateDb();

                #region 检查：登录名、显示名、角色、密码

                var rejected = CheckUserName(name) ?? CheckDisplayName(displayName) ?? CheckPassword(request.Password);
                if (rejected is not null)
                {
                    return HandleResult<UserDto>.Fail(rejected.ErrorMessage, [.. rejected.Args]);
                }

                if (FindUser(db, name) is not null)
                {
                    return HandleResult<UserDto>.Fail(ErrorCodes.AccountUserExists, name);
                }

                var role = db.Queryable<RoleEntity>().InSingle(request.RoleId);
                var roleRejected = CheckAssignable(role, request.RoleId);
                if (roleRejected is not null)
                {
                    return HandleResult<UserDto>.Fail(roleRejected.ErrorMessage, [.. roleRejected.Args]);
                }

                #endregion

                var user = new UserEntity
                {
                    UserName = name,
                    DisplayName = displayName.Length == 0 ? name : displayName,
                    PasswordHash = HashPassword(request.Password),
                    RoleId = request.RoleId
                };
                user.Id = db.Insertable(user).ExecuteReturnBigIdentity();
                var roles = db.Queryable<RoleEntity>().ToList().ToDictionary(item => item.Id);
                result = HandleResult<UserDto>.Success(ToUserDto(user, roles));
                LogHelper.Info(FullPath, $"新建账号 {name}（{role!.Name}）");
            }
            catch (Exception exception)
            {
                return Failed<UserDto>("新建账号", exception);
            }
        }

        NotifyChanged();
        return result;
    }

    public HandleResult UpdateUser(UserUpdateRequest request)
    {
        string displayName = (request.DisplayName ?? string.Empty).Trim();
        lock (_gate)
        {
            if (!_ready)
            {
                return NotReady();
            }

            try
            {
                using var db = CreateDb();
                var user = db.Queryable<UserEntity>().InSingle(request.Id);
                if (user is null)
                {
                    return HandleResult.Fail(ErrorCodes.AccountUserNotFound, request.Id.ToString(CultureInfo.InvariantCulture));
                }

                var rejected = CheckDisplayName(displayName);
                if (rejected is not null)
                {
                    return rejected;
                }

                if (user.IsBuiltIn && request.RoleId != user.RoleId)
                {
                    return HandleResult.Fail(ErrorCodes.AccountBuiltInUser, user.UserName);
                }

                var role = db.Queryable<RoleEntity>().InSingle(request.RoleId);
                var roleRejected = CheckAssignable(role, request.RoleId);
                if (roleRejected is not null)
                {
                    return roleRejected;
                }

                user.DisplayName = displayName.Length == 0 ? user.UserName : displayName;
                user.RoleId = request.RoleId;
                user.UpdatedTime = DateTime.Now;
                db.Updateable(user).UpdateColumns(item => new { item.DisplayName, item.RoleId, item.UpdatedTime }).ExecuteCommand();
                LogHelper.Info(FullPath, $"改了账号 {user.UserName}：显示名 {user.DisplayName}，角色 {role!.Name}");
            }
            catch (Exception exception)
            {
                return Failed("改账号", exception);
            }
        }

        NotifyChanged();
        return HandleResult.Success();
    }

    public HandleResult ResetPassword(UserPasswordResetRequest request)
    {
        lock (_gate)
        {
            if (!_ready)
            {
                return NotReady();
            }

            try
            {
                using var db = CreateDb();
                var user = db.Queryable<UserEntity>().InSingle(request.Id);
                if (user is null)
                {
                    return HandleResult.Fail(ErrorCodes.AccountUserNotFound, request.Id.ToString(CultureInfo.InvariantCulture));
                }

                var rejected = CheckPassword(request.Password);
                if (rejected is not null)
                {
                    return rejected;
                }

                user.PasswordHash = HashPassword(request.Password);
                user.UpdatedTime = DateTime.Now;
                db.Updateable(user).UpdateColumns(item => new { item.PasswordHash, item.UpdatedTime }).ExecuteCommand();
                LogHelper.Info(FullPath, $"重置了账号 {user.UserName} 的密码");
                return HandleResult.Success();
            }
            catch (Exception exception)
            {
                return Failed("重置密码", exception);
            }
        }
    }

    public HandleResult SetUserEnabled(UserEnableRequest request)
    {
        lock (_gate)
        {
            if (!_ready)
            {
                return NotReady();
            }

            try
            {
                using var db = CreateDb();
                var user = db.Queryable<UserEntity>().InSingle(request.Id);
                if (user is null)
                {
                    return HandleResult.Fail(ErrorCodes.AccountUserNotFound, request.Id.ToString(CultureInfo.InvariantCulture));
                }

                if (user.IsBuiltIn && !request.IsEnabled)
                {
                    return HandleResult.Fail(ErrorCodes.AccountBuiltInUser, user.UserName);
                }

                if (user.IsEnabled == request.IsEnabled)
                {
                    return HandleResult.Success();
                }

                user.IsEnabled = request.IsEnabled;
                user.UpdatedTime = DateTime.Now;
                db.Updateable(user).UpdateColumns(item => new { item.IsEnabled, item.UpdatedTime }).ExecuteCommand();
                LogHelper.Info(FullPath, request.IsEnabled ? $"启用了账号 {user.UserName}" : $"停用了账号 {user.UserName}");
            }
            catch (Exception exception)
            {
                return Failed("停用 / 启用账号", exception);
            }
        }

        NotifyChanged();
        return HandleResult.Success();
    }

    public HandleResult DeleteUser(long id)
    {
        lock (_gate)
        {
            if (!_ready)
            {
                return NotReady();
            }

            try
            {
                using var db = CreateDb();
                var user = db.Queryable<UserEntity>().InSingle(id);
                if (user is null)
                {
                    return HandleResult.Fail(ErrorCodes.AccountUserNotFound, id.ToString(CultureInfo.InvariantCulture));
                }

                if (user.IsBuiltIn)
                {
                    return HandleResult.Fail(ErrorCodes.AccountBuiltInUser, user.UserName);
                }

                db.Deleteable<UserEntity>().In(id).ExecuteCommand();
                LogHelper.Info(FullPath, $"删了账号 {user.UserName}");
            }
            catch (Exception exception)
            {
                return Failed("删账号", exception);
            }
        }

        NotifyChanged();
        return HandleResult.Success();
    }

    private static UserEntity? FindUser(SqlSugarClient db, string name)
    {
        if (name.Length == 0)
        {
            return null;
        }

        // 账号没几个，取出来按不分大小写比（库里比要看排序规则）
        return db.Queryable<UserEntity>().ToList()
            .FirstOrDefault(user => string.Equals(user.UserName, name, StringComparison.OrdinalIgnoreCase));
    }

    private static UserDto ToUserDto(UserEntity user, IReadOnlyDictionary<long, RoleEntity> roles)
    {
        roles.TryGetValue(user.RoleId, out var role);
        return new UserDto
        {
            Id = user.Id,
            UserName = user.UserName,
            DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName : user.DisplayName,
            RoleId = user.RoleId,
            RoleName = role?.Name ?? string.Empty,
            RoleKind = role is null ? RoleKind.Normal : (RoleKind)role.Kind,
            IsEnabled = user.IsEnabled,
            IsBuiltIn = user.IsBuiltIn,
            LastLoginTime = user.LastLoginTime
        };
    }

    #endregion

    #region 角色

    public HandleResult<IReadOnlyList<RoleDto>> GetRoles()
    {
        lock (_gate)
        {
            if (!_ready)
            {
                return NotReady<IReadOnlyList<RoleDto>>();
            }

            try
            {
                using var db = CreateDb();
                var counts = db.Queryable<UserEntity>().ToList()
                    .GroupBy(user => user.RoleId)
                    .ToDictionary(group => group.Key, group => group.Count());
                IReadOnlyList<RoleDto> roles = db.Queryable<RoleEntity>().ToList()
                    .OrderBy(role => RoleOrder(role.Kind))
                    .ThenBy(role => role.Id)
                    .Select(role => ToRoleDto(role, counts.GetValueOrDefault(role.Id)))
                    .ToList();
                return HandleResult<IReadOnlyList<RoleDto>>.Success(roles);
            }
            catch (Exception exception)
            {
                return Failed<IReadOnlyList<RoleDto>>("查角色", exception);
            }
        }
    }

    public HandleResult<RoleDto> CreateRole(RoleSaveRequest request)
    {
        string name = (request.Name ?? string.Empty).Trim();
        string description = (request.Description ?? string.Empty).Trim();
        HandleResult<RoleDto> result;
        lock (_gate)
        {
            if (!_ready)
            {
                return NotReady<RoleDto>();
            }

            try
            {
                using var db = CreateDb();
                var rejected = CheckRole(db, 0, name, description);
                if (rejected is not null)
                {
                    return HandleResult<RoleDto>.Fail(rejected.ErrorMessage, [.. rejected.Args]);
                }

                var role = InsertRole(db, name, description, RoleKind.Normal, null);
                result = HandleResult<RoleDto>.Success(ToRoleDto(role, 0));
                LogHelper.Info(FullPath, $"新建角色 {name}");
            }
            catch (Exception exception)
            {
                return Failed<RoleDto>("新建角色", exception);
            }
        }

        NotifyChanged();
        return result;
    }

    public HandleResult UpdateRole(RoleSaveRequest request)
    {
        string name = (request.Name ?? string.Empty).Trim();
        string description = (request.Description ?? string.Empty).Trim();
        lock (_gate)
        {
            if (!_ready)
            {
                return NotReady();
            }

            try
            {
                using var db = CreateDb();
                var role = db.Queryable<RoleEntity>().InSingle(request.Id);
                if (role is null)
                {
                    return HandleResult.Fail(ErrorCodes.AccountRoleNotFound, request.Id.ToString(CultureInfo.InvariantCulture));
                }

                if (role.Kind != (int)RoleKind.Normal)
                {
                    return HandleResult.Fail(ErrorCodes.AccountBuiltInRole, role.Name);
                }

                var rejected = CheckRole(db, role.Id, name, description);
                if (rejected is not null)
                {
                    return rejected;
                }

                string before = role.Name;
                role.Name = name;
                role.Description = description;
                role.UpdatedTime = DateTime.Now;
                db.Updateable(role).UpdateColumns(item => new { item.Name, item.Description, item.UpdatedTime }).ExecuteCommand();
                LogHelper.Info(FullPath, $"改了角色 {before}：名字 {name}，说明 {description}");
            }
            catch (Exception exception)
            {
                return Failed("改角色", exception);
            }
        }

        NotifyChanged();
        return HandleResult.Success();
    }

    public HandleResult DeleteRole(long id)
    {
        lock (_gate)
        {
            if (!_ready)
            {
                return NotReady();
            }

            try
            {
                using var db = CreateDb();
                var role = db.Queryable<RoleEntity>().InSingle(id);
                if (role is null)
                {
                    return HandleResult.Fail(ErrorCodes.AccountRoleNotFound, id.ToString(CultureInfo.InvariantCulture));
                }

                if (role.Kind != (int)RoleKind.Normal)
                {
                    return HandleResult.Fail(ErrorCodes.AccountBuiltInRole, role.Name);
                }

                int users = db.Queryable<UserEntity>().Count(user => user.RoleId == id);
                if (users > 0)
                {
                    return HandleResult.Fail(ErrorCodes.AccountRoleInUse, role.Name, users.ToString(CultureInfo.InvariantCulture));
                }

                var deleted = db.Ado.UseTran(() =>
                {
                    db.Deleteable<RolePermissionEntity>().Where(item => item.RoleId == id).ExecuteCommand();
                    db.Deleteable<RoleEntity>().In(id).ExecuteCommand();
                });
                if (!deleted.IsSuccess)
                {
                    LogHelper.Error(FullPath, $"删角色 {role.Name} 时读写账号库出错：{deleted.ErrorMessage}");
                    return HandleResult.Fail(ErrorCodes.AccountFailed, deleted.ErrorMessage);
                }

                LogHelper.Info(FullPath, $"删了角色 {role.Name}");
            }
            catch (Exception exception)
            {
                return Failed("删角色", exception);
            }
        }

        NotifyChanged();
        return HandleResult.Success();
    }

    public HandleResult<RolePermissionsDto> GetPermissions(long roleId)
    {
        lock (_gate)
        {
            if (!_ready)
            {
                return NotReady<RolePermissionsDto>();
            }

            try
            {
                using var db = CreateDb();
                var role = db.Queryable<RoleEntity>().InSingle(roleId);
                if (role is null)
                {
                    return HandleResult<RolePermissionsDto>.Fail(ErrorCodes.AccountRoleNotFound, roleId.ToString(CultureInfo.InvariantCulture));
                }

                var permissions = new RolePermissionsDto
                {
                    RoleId = role.Id,
                    RoleKind = (RoleKind)role.Kind,
                    Items = db.Queryable<RolePermissionEntity>()
                        .Where(item => item.RoleId == roleId)
                        .ToList()
                        .Select(item => new RolePermissionItem { Code = item.Code, Level = (PermissionLevel)item.Level })
                        .ToList()
                };
                return HandleResult<RolePermissionsDto>.Success(permissions);
            }
            catch (Exception exception)
            {
                return Failed<RolePermissionsDto>("查角色权限", exception);
            }
        }
    }

    public HandleResult SavePermissions(RolePermissionsSaveRequest request)
    {
        lock (_gate)
        {
            if (!_ready)
            {
                return NotReady();
            }

            try
            {
                using var db = CreateDb();
                var role = db.Queryable<RoleEntity>().InSingle(request.RoleId);
                if (role is null)
                {
                    return HandleResult.Fail(ErrorCodes.AccountRoleNotFound, request.RoleId.ToString(CultureInfo.InvariantCulture));
                }

                if (role.Kind == (int)RoleKind.Admin)
                {
                    return HandleResult.Fail(ErrorCodes.AccountBuiltInRole, role.Name);
                }

                // 同一项给了几次的以最后一次为准
                var levels = new Dictionary<string, PermissionLevel>(StringComparer.Ordinal);
                foreach (var item in request.Items)
                {
                    string code = (item.Code ?? string.Empty).Trim();
                    if (code.Length == 0 || code.Length > CodeMaxLength || !Enum.IsDefined(item.Level))
                    {
                        return HandleResult.Fail(ErrorCodes.AccountPermissionInvalid, code, ((int)item.Level).ToString(CultureInfo.InvariantCulture));
                    }

                    levels[code] = item.Level;
                }

                var rows = levels.Select(item => new RolePermissionEntity
                {
                    RoleId = role.Id,
                    Code = item.Key,
                    Level = (int)item.Value
                }).ToList();
                var saved = db.Ado.UseTran(() =>
                {
                    db.Deleteable<RolePermissionEntity>().Where(item => item.RoleId == role.Id).ExecuteCommand();
                    if (rows.Count > 0)
                    {
                        db.Insertable(rows).ExecuteCommand();
                    }
                });
                if (!saved.IsSuccess)
                {
                    LogHelper.Error(FullPath, $"存角色 {role.Name} 的权限时读写账号库出错：{saved.ErrorMessage}");
                    return HandleResult.Fail(ErrorCodes.AccountFailed, saved.ErrorMessage);
                }

                LogHelper.Info(FullPath, $"改了角色 {role.Name} 的权限（{rows.Count} 项）");
            }
            catch (Exception exception)
            {
                return Failed("存角色权限", exception);
            }
        }

        NotifyChanged();
        return HandleResult.Success();
    }

    /// <summary>
    /// 角色名不能空、不能太长、不能跟别的角色重名（不分大小写），说明不能太长
    /// </summary>
    private static HandleResult? CheckRole(SqlSugarClient db, long selfId, string name, string description)
    {
        if (name.Length == 0 || name.Length > RoleNameMaxLength)
        {
            return HandleResult.Fail(ErrorCodes.AccountRoleNameInvalid, RoleNameMaxLength.ToString(CultureInfo.InvariantCulture));
        }

        if (description.Length > DescriptionMaxLength)
        {
            return HandleResult.Fail(ErrorCodes.AccountRoleDescriptionInvalid, DescriptionMaxLength.ToString(CultureInfo.InvariantCulture));
        }

        bool exists = db.Queryable<RoleEntity>().ToList()
            .Any(role => role.Id != selfId && string.Equals(role.Name, name, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            return HandleResult.Fail(ErrorCodes.AccountRoleExists, name);
        }

        return null;
    }

    /// <summary>
    /// 账号能不能用这个角色：要有这个角色，访客角色不给账号用
    /// </summary>
    private static HandleResult? CheckAssignable(RoleEntity? role, long roleId)
    {
        if (role is null)
        {
            return HandleResult.Fail(ErrorCodes.AccountRoleNotFound, roleId.ToString(CultureInfo.InvariantCulture));
        }

        if (role.Kind == (int)RoleKind.Guest)
        {
            return HandleResult.Fail(ErrorCodes.AccountRoleNotAssignable, role.Name);
        }

        return null;
    }

    /// <summary>
    /// 角色的先后：管理员在前，自己建的在中间，访客在后
    /// </summary>
    private static int RoleOrder(int kind)
    {
        switch ((RoleKind)kind)
        {
            case RoleKind.Admin:
                return 0;
            case RoleKind.Guest:
                return 2;
            default:
                return 1;
        }
    }

    private static RoleDto ToRoleDto(RoleEntity role, int userCount)
    {
        return new RoleDto
        {
            Id = role.Id,
            Name = role.Name,
            Description = role.Description ?? string.Empty,
            Kind = (RoleKind)role.Kind,
            UserCount = userCount
        };
    }

    #endregion

    #region 检查：登录名、显示名、密码

    private static HandleResult? CheckUserName(string name)
    {
        if (name.Length == 0 || name.Length > UserNameMaxLength || name.Any(char.IsWhiteSpace))
        {
            return HandleResult.Fail(ErrorCodes.AccountUserNameInvalid, UserNameMaxLength.ToString(CultureInfo.InvariantCulture));
        }

        return null;
    }

    private static HandleResult? CheckDisplayName(string displayName)
    {
        if (displayName.Length > DisplayNameMaxLength)
        {
            return HandleResult.Fail(ErrorCodes.AccountDisplayNameInvalid, DisplayNameMaxLength.ToString(CultureInfo.InvariantCulture));
        }

        return null;
    }

    private HandleResult? CheckPassword(string? password)
    {
        int minLength = PasswordMinLength;
        if ((password ?? string.Empty).Length < minLength)
        {
            return HandleResult.Fail(ErrorCodes.AccountPasswordTooShort, minLength.ToString(CultureInfo.InvariantCulture));
        }

        return null;
    }

    #endregion

    #region 密码哈希

    /// <summary>
    /// 加盐哈希：算法名$迭代次数$盐$哈希（盐、哈希是 Base64）
    /// </summary>
    private static string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, HashIterations, HashAlgorithmName.SHA256, HashBytes);
        return string.Join(HashSeparator, HashScheme, HashIterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <summary>
    /// 核对密码：哈希串写法不对的一律当不对
    /// </summary>
    private static bool VerifyPassword(string password, string stored)
    {
        string[] parts = (stored ?? string.Empty).Split(HashSeparator);
        if (parts.Length != HashParts || !string.Equals(parts[0], HashScheme, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int iterations) || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (expected.Length == 0)
        {
            return false;
        }

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    #endregion

    #region 公共

    private SqlSugarClient CreateDb()
    {
        return XyzDb.Create(Database);
    }

    /// <summary>
    /// 账号、角色、权限变了：推给客户端，登录着的重新取自己的会话
    /// </summary>
    private static void NotifyChanged()
    {
        EventBus.Send(new AccountChangedDto(), AccountChangedDto.EventToken, retain: false);
    }

    private HandleResult NotReady()
    {
        return HandleResult.Fail(ErrorCodes.AccountFailed, $"{FullPath} 开机建表没做成，见日志");
    }

    private HandleResult<T> NotReady<T>()
    {
        return HandleResult<T>.Fail(ErrorCodes.AccountFailed, $"{FullPath} 开机建表没做成，见日志");
    }

    private HandleResult Failed(string what, Exception exception)
    {
        LogHelper.Error(FullPath, $"{what}时读写账号库出错：{exception.Message}");
        return HandleResult.Fail(ErrorCodes.AccountFailed, exception.Message);
    }

    private HandleResult<T> Failed<T>(string what, Exception exception)
    {
        LogHelper.Error(FullPath, $"{what}时读写账号库出错：{exception.Message}");
        return HandleResult<T>.Fail(ErrorCodes.AccountFailed, exception.Message);
    }

    #endregion
}
