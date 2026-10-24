using SqlSugar;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Configs.Models;
using xyz.Database.Auth;
using xyz.Database.DbProvider;
using xyz.Service.Systems;
using xyz.Service.UserManger;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Permissions;
using xyz.Shared.Rpc;
using xyz.Tools;

// 账号冒烟：账号组件（machine.xml 的 Account 节点）开机建表、删老的 User / Role / Menu 表、预置管理员 / 访客 / 工程师 / 操作员四个角色和内置账号 admin（只预置一次，重开不重复）；
// 登录（对、错、不分大小写、停用、记最后登录）；会话（访客、普通角色带档位、管理员不带档位）；密码只存加盐哈希；改密码、重置密码；
// 账号的增删改（登录名 / 显示名不对、重名、密码太短、角色不在、访客角色不给账号、内置 admin 不能删 / 停用 / 改角色）；
// 角色（重名、名字 / 说明不对、内置的不能改名和删、有账号在用不能删、删角色连权限一起删、管理员权限不能改、权限整张换、档位不对、同一项以最后一次为准）；
// 变了推 AccountChangedDto（登录、查询不推）；三个服务（没配回 account.not_installed、错误码原样回、数据转 DTO）；系统设置带登录开关；账号库建不起来时回 account.failed。

SmokeIoc.Reset();

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("FAIL: " + message);
    }

    checks++;
}

const string DbName = "AccountSmoke";
string dbPath = Path.Combine(Path.GetTempPath(), $"account-smoke-{Guid.NewGuid():N}.db");
XyzDb.Register(DbName, $"DataSource={dbPath}", DbType.Sqlite);

int changed = 0;
using var subscription = EventBus.Register<AccountChangedDto>(AccountChangedDto.EventToken, _ => changed++);

try
{
    // 1. 装配：按 IAccount 进容器；machine.xml 默认要登录、库照 machine.xml、闲置 15 分钟自动退出、密码最短 4 位（这两个放 machine.xml 不放 EC）；
    //    闲置分钟数、密码最短几位配错开机就报
    string? LoadError(params ValueConfig[] values)
    {
        try
        {
            ComponentLoader.Load([Node("Account", typeof(AccountComponent), values)]);
            return null;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }

    string? error = LoadError(Value("IdleLogoutMinutes", "-1"));
    Check(error is not null && error.Contains("IdleLogoutMinutes=-1", StringComparison.Ordinal), "闲置分钟数配成负的：装配就报，带上配的值");
    error = LoadError(Value("IdleLogoutMinutes", "1441"));
    Check(error is not null && error.Contains("IdleLogoutMinutes", StringComparison.Ordinal), "闲置分钟数超过一天：装配就报");
    error = LoadError(Value("PasswordMinLength", "0"));
    Check(error is not null && error.Contains("PasswordMinLength", StringComparison.Ordinal), "密码最短配成 0：装配就报");
    error = LoadError(Value("PasswordMinLength", "33"));
    Check(error is not null && error.Contains("PasswordMinLength", StringComparison.Ordinal), "密码最短超过 32：装配就报");
    Check(LoadError(Value("IdleLogoutMinutes", "0"), Value("PasswordMinLength", "8")) is null, "闲置配 0（不自动退出）、密码最短 8 位能装上");

    var roots = SmokeIoc.Load([Node("Account", typeof(AccountComponent), Value("Database", DbName))]);
    var account = (AccountComponent)roots[0];
    Check(ReferenceEquals(IocHelper.GetService<IAccount>(), account), "按 IAccount 找得到账号组件");
    Check(account.IsEnable && account.Database == DbName, "machine.xml：IsEnable 默认 True，库照 machine.xml");
    Check(account.IdleLogoutMinutes == 15 && account.PasswordMinLength == 4, "machine.xml 默认：闲置 15 分钟自动退出、密码最短 4 位");
    Check(typeof(AccountComponent).GetProperty(nameof(AccountComponent.IdleLogoutMinutes))!.GetCustomAttributes(typeof(VariableMarkAttribute), false).Length == 0
        && typeof(AccountComponent).GetProperty(nameof(AccountComponent.PasswordMinLength))!.GetCustomAttributes(typeof(VariableMarkAttribute), false).Length == 0,
        "闲置分钟数、密码最短几位不是 EC（EC 页改不了）");

    // 2. 开机建表、预置：四个角色（管理员在前、访客在后）、内置账号 admin；预置的档位。
    //    库里留着换 account_* 以前老服务建的 User、Role、Menu 表：开机删掉，名字只是像的表不动
    using (var db = XyzDb.Create(DbName))
    {
        db.Ado.ExecuteCommand("CREATE TABLE \"User\" (Id INTEGER PRIMARY KEY, UserName TEXT)");
        db.Ado.ExecuteCommand("CREATE TABLE \"Role\" (Id INTEGER PRIMARY KEY, Name TEXT)");
        db.Ado.ExecuteCommand("CREATE TABLE \"Menu\" (Id INTEGER PRIMARY KEY, Code TEXT)");
        db.Ado.ExecuteCommand("CREATE TABLE \"UserNote\" (Id INTEGER PRIMARY KEY)");
        db.Ado.ExecuteCommand("INSERT INTO \"User\" (UserName) VALUES ('old')");
    }

    Check(account.InitComponent(), "组件初始化：建表、预置做成");
    using (var db = XyzDb.Create(DbName))
    {
        Check(!db.DbMaintenance.IsAnyTable("User", false) && !db.DbMaintenance.IsAnyTable("Role", false) && !db.DbMaintenance.IsAnyTable("Menu", false),
            "开机删掉老的账号、菜单表 User、Role、Menu");
        Check(db.DbMaintenance.IsAnyTable("UserNote", false) && db.DbMaintenance.IsAnyTable("account_user", false), "名字只是像的表、新的账号表不动");
    }

    var roles = account.GetRoles().Result!;
    Check(roles.Count == 4
        && roles[0].Kind == RoleKind.Admin && roles[0].Name == "管理员"
        && roles[1].Kind == RoleKind.Normal && roles[1].Name == "工程师"
        && roles[2].Kind == RoleKind.Normal && roles[2].Name == "操作员"
        && roles[3].Kind == RoleKind.Guest && roles[3].Name == "访客",
        "预置四个角色：管理员、工程师、操作员、访客（管理员在前、访客在后）");
    long adminRoleId = roles[0].Id;
    long engineerRoleId = roles[1].Id;
    long operatorRoleId = roles[2].Id;
    long guestRoleId = roles[3].Id;
    var users = account.GetUsers().Result!;
    Check(users.Count == 1 && users[0].UserName == AccountComponent.AdminUserName && users[0].IsBuiltIn && users[0].IsEnabled
        && users[0].RoleId == adminRoleId && users[0].RoleKind == RoleKind.Admin && roles[0].UserCount == 1,
        "预置内置账号 admin：启用、管理员角色");

    var guest = account.GetSession(string.Empty).Result!;
    Check(guest.UserName.Length == 0 && guest.RoleKind == RoleKind.Guest
        && guest.Permissions[PermissionCodes.Main] == PermissionLevel.View
        && guest.Permissions[PermissionCodes.MainSystem] == PermissionLevel.View
        && guest.Permissions[PermissionCodes.ManualLoadPorts] == PermissionLevel.Hidden
        && guest.Permissions[PermissionCodes.RecipeProcess] == PermissionLevel.Hidden
        && guest.Permissions[PermissionCodes.AlarmRealtime] == PermissionLevel.View
        && guest.Permissions[PermissionCodes.AlarmReset] == PermissionLevel.View
        && guest.Permissions[PermissionCodes.DataCenterDataChart] == PermissionLevel.View
        && guest.Permissions[PermissionCodes.SettingEc] == PermissionLevel.Hidden
        && guest.Permissions[PermissionCodes.Io] == PermissionLevel.Hidden,
        "访客：主界面、报警、数据中心只看，整机控制、报警复位不允许，手动、配方、设置、IO 隐藏");
    Check(!guest.Permissions.ContainsKey(PermissionCodes.SettingUser) && !guest.Permissions.ContainsKey(PermissionCodes.SettingRole),
        "用户管理、角色管理固定只给管理员，不进库");

    var engineer = PermissionsOf(account, engineerRoleId);
    Check(engineer[PermissionCodes.Main] == PermissionLevel.Operate && engineer[PermissionCodes.MainSystem] == PermissionLevel.Operate
        && engineer[PermissionCodes.ManualChamber] == PermissionLevel.Operate && engineer[PermissionCodes.RecipeSequence] == PermissionLevel.Operate
        && engineer[PermissionCodes.AlarmHistory] == PermissionLevel.View && engineer[PermissionCodes.AlarmReset] == PermissionLevel.Operate
        && engineer[PermissionCodes.SettingWaferLedger] == PermissionLevel.Operate && engineer[PermissionCodes.Io] == PermissionLevel.Operate,
        "工程师：主界面、手动、配方、EC、账单调整、IO 能操作，报警只看，整机控制、报警复位允许");
    var operatorLevels = PermissionsOf(account, operatorRoleId);
    Check(operatorLevels[PermissionCodes.Main] == PermissionLevel.Operate && operatorLevels[PermissionCodes.MainSystem] == PermissionLevel.Operate
        && operatorLevels[PermissionCodes.ManualRobot] == PermissionLevel.View && operatorLevels[PermissionCodes.RecipeProcess] == PermissionLevel.View
        && operatorLevels[PermissionCodes.AlarmReset] == PermissionLevel.Operate && operatorLevels[PermissionCodes.SettingEc] == PermissionLevel.Hidden
        && operatorLevels[PermissionCodes.Io] == PermissionLevel.Hidden,
        "操作员：主界面能操作、整机控制和报警复位允许，手动、配方只看，设置、IO 隐藏（IO 是工程师级别）");

    // 3. 重开（新组件、同一个库）：不重复预置
    var again = new AccountComponent { Database = DbName, IdleLogoutMinutes = 7 };
    Check(again.InitComponent() && again.GetRoles().Result!.Count == 4 && again.GetUsers().Result!.Count == 1, "重开同一个库：角色还是 4 个、账号还是 1 个");
    Check(again.GetSession(string.Empty).Result!.IdleLogoutMinutes == 7, "machine.xml 配的闲置分钟数跟着会话给客户端");

    // 4. 登录：对的、不分大小写、错的、没有这个人；记最后登录
    changed = 0;
    var login = account.Login(AccountComponent.AdminUserName, AccountComponent.AdminInitialPassword);
    Check(login.IsSuccess && login.Result!.UserName == AccountComponent.AdminUserName && login.Result.RoleKind == RoleKind.Admin
        && login.Result.Permissions.Count == 0, "admin / admin 登录：管理员，不带档位（全部能操作）");
    Check(login.Result!.IdleLogoutMinutes == 15 && account.GetSession(string.Empty).Result!.IdleLogoutMinutes == 15, "会话带闲置自动退出的分钟数（machine.xml）");
    Check(account.Login("ADMIN", AccountComponent.AdminInitialPassword).IsSuccess, "登录名不分大小写");
    Check(account.Login(" admin ", AccountComponent.AdminInitialPassword).IsSuccess, "登录名前后的空格去掉");
    login = account.Login(AccountComponent.AdminUserName, "wrong");
    Check(!login.IsSuccess && login.ErrorMessage == ErrorCodes.AccountLoginFailed && login.Args.Count == 0, "密码不对：account.login_failed，不说是哪一样不对");
    login = account.Login("nobody", "whatever");
    Check(!login.IsSuccess && login.ErrorMessage == ErrorCodes.AccountLoginFailed, "没有这个人：同样回 account.login_failed");
    Check(account.GetUsers().Result![0].LastLoginTime is not null, "登录记下最后登录时间");
    Check(changed == 0, "登录、查询不推账号变了");

    // 5. 密码只存加盐哈希：看不到原文，同一个密码两次哈希不一样
    using (var db = XyzDb.Create(DbName))
    {
        var adminRow = db.Queryable<UserEntity>().First(user => user.IsBuiltIn);
        Check(adminRow.PasswordHash.StartsWith("PBKDF2-SHA256$", StringComparison.Ordinal)
            && !adminRow.PasswordHash.Contains(AccountComponent.AdminInitialPassword, StringComparison.Ordinal),
            "库里存的是 PBKDF2 加盐哈希，没有原文");
    }

    // 6. 建账号：各项检查、建好能登录，会话带角色的档位
    changed = 0;
    var created = account.CreateUser(new UserCreateRequest { UserName = string.Empty, RoleId = operatorRoleId, Password = "1234" });
    Check(!created.IsSuccess && created.ErrorMessage == ErrorCodes.AccountUserNameInvalid && created.Args.SequenceEqual(["50"]), "登录名空：account.user_name_invalid [最长 50]");
    created = account.CreateUser(new UserCreateRequest { UserName = "op 01", RoleId = operatorRoleId, Password = "1234" });
    Check(!created.IsSuccess && created.ErrorMessage == ErrorCodes.AccountUserNameInvalid, "登录名带空格：account.user_name_invalid");
    created = account.CreateUser(new UserCreateRequest { UserName = new string('a', 51), RoleId = operatorRoleId, Password = "1234" });
    Check(!created.IsSuccess && created.ErrorMessage == ErrorCodes.AccountUserNameInvalid, "登录名超过 50 个字：account.user_name_invalid");
    created = account.CreateUser(new UserCreateRequest { UserName = "op01", DisplayName = new string('名', 101), RoleId = operatorRoleId, Password = "1234" });
    Check(!created.IsSuccess && created.ErrorMessage == ErrorCodes.AccountDisplayNameInvalid && created.Args.SequenceEqual(["100"]), "显示名超过 100 个字：account.display_name_invalid [100]");
    created = account.CreateUser(new UserCreateRequest { UserName = "op01", RoleId = operatorRoleId, Password = "123" });
    Check(!created.IsSuccess && created.ErrorMessage == ErrorCodes.AccountPasswordTooShort && created.Args.SequenceEqual(["4"]), "密码 3 位：account.password_too_short [4]");
    created = account.CreateUser(new UserCreateRequest { UserName = "op01", RoleId = 9999, Password = "1234" });
    Check(!created.IsSuccess && created.ErrorMessage == ErrorCodes.AccountRoleNotFound && created.Args.SequenceEqual(["9999"]), "角色不在：account.role_not_found [编号]");
    created = account.CreateUser(new UserCreateRequest { UserName = "op01", RoleId = guestRoleId, Password = "1234" });
    Check(!created.IsSuccess && created.ErrorMessage == ErrorCodes.AccountRoleNotAssignable && created.Args.SequenceEqual(["访客"]), "访客角色不给账号：account.role_not_assignable [角色名]");
    created = account.CreateUser(new UserCreateRequest { UserName = "Admin", RoleId = operatorRoleId, Password = "1234" });
    Check(!created.IsSuccess && created.ErrorMessage == ErrorCodes.AccountUserExists && created.Args.SequenceEqual(["Admin"]), "登录名重了（不分大小写）：account.user_exists [登录名]");
    Check(changed == 0, "没建成不推");

    created = account.CreateUser(new UserCreateRequest { UserName = " op01 ", DisplayName = "王师傅", RoleId = operatorRoleId, Password = "1234" });
    Check(created.IsSuccess && created.Result!.UserName == "op01" && created.Result.DisplayName == "王师傅" && created.Result.RoleName == "操作员"
        && created.Result.RoleKind == RoleKind.Normal && created.Result.IsEnabled && !created.Result.IsBuiltIn && created.Result.LastLoginTime is null,
        "建账号 op01：去掉前后空格，带角色名、启用、没登录过");
    Check(changed == 1, "建了账号推一次");
    long op01 = created.Result!.Id;
    var nameOnly = account.CreateUser(new UserCreateRequest { UserName = "op02", RoleId = operatorRoleId, Password = "1234" });
    Check(nameOnly.IsSuccess && nameOnly.Result!.DisplayName == "op02", "没填显示名：显示名就是登录名");
    using (var db = XyzDb.Create(DbName))
    {
        var hashes = db.Queryable<UserEntity>().Where(user => user.UserName == "op01" || user.UserName == "op02").ToList();
        Check(hashes.Count == 2 && hashes[0].PasswordHash != hashes[1].PasswordHash, "两个账号密码一样、哈希不一样（各自的盐）");
    }

    login = account.Login("op01", "1234");
    Check(login.IsSuccess && login.Result!.DisplayName == "王师傅" && login.Result.RoleKind == RoleKind.Normal
        && login.Result.Permissions[PermissionCodes.ManualRobot] == PermissionLevel.View
        && login.Result.Permissions[PermissionCodes.Io] == PermissionLevel.Hidden,
        "op01 登录：会话带操作员的档位");
    Check(account.GetRoles().Result!.Single(role => role.Id == operatorRoleId).UserCount == 2, "角色带账号数");

    // 7. 改账号：显示名、角色；内置 admin 不能改角色（显示名能改）；访客角色不给账号
    changed = 0;
    var updated = account.UpdateUser(new UserUpdateRequest { Id = op01, DisplayName = "王工", RoleId = engineerRoleId });
    var session = account.GetSession("op01");
    Check(updated.IsSuccess && session.IsSuccess && session.Result!.DisplayName == "王工" && session.Result.RoleName == "工程师"
        && session.Result.Permissions[PermissionCodes.Io] == PermissionLevel.Operate, "op01 改成工程师：会话按新角色");
    Check(changed == 1, "改了账号推一次");
    long adminId = account.GetUsers().Result!.Single(user => user.IsBuiltIn).Id;
    updated = account.UpdateUser(new UserUpdateRequest { Id = adminId, DisplayName = "管理员", RoleId = operatorRoleId });
    Check(!updated.IsSuccess && updated.ErrorMessage == ErrorCodes.AccountBuiltInUser && updated.Args.SequenceEqual([AccountComponent.AdminUserName]), "内置 admin 改角色：account.builtin_user");
    updated = account.UpdateUser(new UserUpdateRequest { Id = adminId, DisplayName = "系统管理员", RoleId = adminRoleId });
    Check(updated.IsSuccess && account.GetSession(AccountComponent.AdminUserName).Result!.DisplayName == "系统管理员", "内置 admin 能改显示名");
    updated = account.UpdateUser(new UserUpdateRequest { Id = op01, DisplayName = "王工", RoleId = guestRoleId });
    Check(!updated.IsSuccess && updated.ErrorMessage == ErrorCodes.AccountRoleNotAssignable, "改成访客角色：account.role_not_assignable");
    updated = account.UpdateUser(new UserUpdateRequest { Id = 9999, DisplayName = "x", RoleId = engineerRoleId });
    Check(!updated.IsSuccess && updated.ErrorMessage == ErrorCodes.AccountUserNotFound && updated.Args.SequenceEqual(["9999"]), "没有这个账号：account.user_not_found [编号]");

    // 8. 停用 / 启用：停用的登录不了、取会话也回停用；内置 admin 不能停用
    changed = 0;
    Check(account.SetUserEnabled(new UserEnableRequest { Id = op01, IsEnabled = false }).IsSuccess && changed == 1, "停用 op01，推一次");
    login = account.Login("op01", "1234");
    Check(!login.IsSuccess && login.ErrorMessage == ErrorCodes.AccountUserDisabled && login.Args.SequenceEqual(["op01"]), "停用的登录：account.user_disabled [登录名]");
    session = account.GetSession("op01");
    Check(!session.IsSuccess && session.ErrorMessage == ErrorCodes.AccountUserDisabled, "停用的取会话：account.user_disabled（登录着的客户端回访客）");
    Check(account.SetUserEnabled(new UserEnableRequest { Id = op01, IsEnabled = false }).IsSuccess && changed == 1, "已经停用再停用：成功、不再推");
    Check(account.SetUserEnabled(new UserEnableRequest { Id = op01, IsEnabled = true }).IsSuccess && account.Login("op01", "1234").IsSuccess, "启用以后能登录");
    var disabled = account.SetUserEnabled(new UserEnableRequest { Id = adminId, IsEnabled = false });
    Check(!disabled.IsSuccess && disabled.ErrorMessage == ErrorCodes.AccountBuiltInUser, "内置 admin 停用：account.builtin_user");

    // 9. 改自己的密码、管理员重置密码
    var password = account.ChangePassword("op01", "wrong", "5678");
    Check(!password.IsSuccess && password.ErrorMessage == ErrorCodes.AccountPasswordWrong, "旧密码不对：account.password_wrong");
    password = account.ChangePassword("op01", "1234", "56");
    Check(!password.IsSuccess && password.ErrorMessage == ErrorCodes.AccountPasswordTooShort, "新密码太短：account.password_too_short");
    password = account.ChangePassword("op01", "1234", "5678");
    Check(password.IsSuccess && account.Login("op01", "5678").IsSuccess && !account.Login("op01", "1234").IsSuccess, "改了密码：新密码能登、旧的不能");
    password = account.ResetPassword(new UserPasswordResetRequest { Id = op01, Password = "reset1" });
    Check(password.IsSuccess && account.Login("op01", "reset1").IsSuccess, "重置密码（不要旧密码）以后能登");
    password = account.ResetPassword(new UserPasswordResetRequest { Id = op01, Password = "1" });
    Check(!password.IsSuccess && password.ErrorMessage == ErrorCodes.AccountPasswordTooShort, "重置成太短的：account.password_too_short");

    // 10. 角色：建、重名、名字 / 说明不对、改名、内置的不能改名和删、有人在用不能删
    changed = 0;
    var role = account.CreateRole(new RoleSaveRequest { Name = " QC ", Description = "质检" });
    Check(role.IsSuccess && role.Result!.Name == "QC" && role.Result.Kind == RoleKind.Normal && role.Result.UserCount == 0 && changed == 1, "建角色 QC：去掉前后空格，推一次");
    long qcId = role.Result!.Id;
    role = account.CreateRole(new RoleSaveRequest { Name = "qc" });
    Check(!role.IsSuccess && role.ErrorMessage == ErrorCodes.AccountRoleExists && role.Args.SequenceEqual(["qc"]), "角色名重了（不分大小写）：account.role_exists");
    role = account.CreateRole(new RoleSaveRequest { Name = "  " });
    Check(!role.IsSuccess && role.ErrorMessage == ErrorCodes.AccountRoleNameInvalid && role.Args.SequenceEqual(["50"]), "角色名空：account.role_name_invalid [50]");
    role = account.CreateRole(new RoleSaveRequest { Name = "长说明", Description = new string('说', 501) });
    Check(!role.IsSuccess && role.ErrorMessage == ErrorCodes.AccountRoleDescriptionInvalid && role.Args.SequenceEqual(["500"]), "说明超过 500 个字：account.role_description_invalid [500]");
    var renamed = account.UpdateRole(new RoleSaveRequest { Id = qcId, Name = "质检员", Description = "看配方、看报警" });
    Check(renamed.IsSuccess && account.GetRoles().Result!.Single(item => item.Id == qcId).Name == "质检员", "改角色名、说明");
    renamed = account.UpdateRole(new RoleSaveRequest { Id = qcId, Name = "工程师" });
    Check(!renamed.IsSuccess && renamed.ErrorMessage == ErrorCodes.AccountRoleExists, "改成别的角色的名字：account.role_exists");
    renamed = account.UpdateRole(new RoleSaveRequest { Id = adminRoleId, Name = "超级管理员" });
    Check(!renamed.IsSuccess && renamed.ErrorMessage == ErrorCodes.AccountBuiltInRole && renamed.Args.SequenceEqual(["管理员"]), "内置管理员改名：account.builtin_role");
    renamed = account.UpdateRole(new RoleSaveRequest { Id = guestRoleId, Name = "游客" });
    Check(!renamed.IsSuccess && renamed.ErrorMessage == ErrorCodes.AccountBuiltInRole, "内置访客改名：account.builtin_role");
    var deleted = account.DeleteRole(guestRoleId);
    Check(!deleted.IsSuccess && deleted.ErrorMessage == ErrorCodes.AccountBuiltInRole, "删内置访客：account.builtin_role");
    deleted = account.DeleteRole(operatorRoleId);
    Check(!deleted.IsSuccess && deleted.ErrorMessage == ErrorCodes.AccountRoleInUse && deleted.Args.SequenceEqual(["操作员", "1"]), "删有人在用的角色：account.role_in_use [角色名, 账号数]");

    // 11. 权限：整张换、同一项以最后一次为准、档位不对、管理员不能改；访客改了会话跟着变；删角色连权限一起删
    changed = 0;
    Check(account.GetPermissions(qcId).Result!.Items.Count == 0, "新建的角色没配过权限（界面按只看算）");
    var saved = account.SavePermissions(new RolePermissionsSaveRequest
    {
        RoleId = qcId,
        Items =
        [
            new RolePermissionItem { Code = PermissionCodes.RecipeProcess, Level = PermissionLevel.Operate },
            new RolePermissionItem { Code = PermissionCodes.Io, Level = PermissionLevel.Hidden },
            new RolePermissionItem { Code = PermissionCodes.RecipeProcess, Level = PermissionLevel.View },
        ]
    });
    var qc = PermissionsOf(account, qcId);
    Check(saved.IsSuccess && qc.Count == 2 && qc[PermissionCodes.RecipeProcess] == PermissionLevel.View && qc[PermissionCodes.Io] == PermissionLevel.Hidden && changed == 1,
        "存权限：同一项给两次以最后一次为准，推一次");
    saved = account.SavePermissions(new RolePermissionsSaveRequest
    {
        RoleId = qcId,
        Items = [new RolePermissionItem { Code = PermissionCodes.AlarmReset, Level = PermissionLevel.Operate }]
    });
    qc = PermissionsOf(account, qcId);
    Check(saved.IsSuccess && qc.Count == 1 && qc[PermissionCodes.AlarmReset] == PermissionLevel.Operate, "再存一次：整张换掉");
    saved = account.SavePermissions(new RolePermissionsSaveRequest
    {
        RoleId = qcId,
        Items = [new RolePermissionItem { Code = PermissionCodes.Io, Level = (PermissionLevel)9 }]
    });
    Check(!saved.IsSuccess && saved.ErrorMessage == ErrorCodes.AccountPermissionInvalid && saved.Args.SequenceEqual([PermissionCodes.Io, "9"]), "档位不对：account.permission_invalid [编码, 档位]");
    saved = account.SavePermissions(new RolePermissionsSaveRequest
    {
        RoleId = qcId,
        Items = [new RolePermissionItem { Code = " ", Level = PermissionLevel.View }]
    });
    Check(!saved.IsSuccess && saved.ErrorMessage == ErrorCodes.AccountPermissionInvalid, "编码空：account.permission_invalid");
    Check(PermissionsOf(account, qcId).Count == 1, "没存成的不动原来的");
    saved = account.SavePermissions(new RolePermissionsSaveRequest { RoleId = adminRoleId, Items = [] });
    Check(!saved.IsSuccess && saved.ErrorMessage == ErrorCodes.AccountBuiltInRole, "管理员的权限不能改：account.builtin_role");
    saved = account.SavePermissions(new RolePermissionsSaveRequest
    {
        RoleId = guestRoleId,
        Items = [new RolePermissionItem { Code = PermissionCodes.ManualLoadPorts, Level = PermissionLevel.View }]
    });
    guest = account.GetSession(string.Empty).Result!;
    Check(saved.IsSuccess && guest.Permissions.Count == 1 && guest.Permissions[PermissionCodes.ManualLoadPorts] == PermissionLevel.View, "访客的权限能改，会话跟着变");
    deleted = account.DeleteRole(qcId);
    Check(deleted.IsSuccess && account.GetRoles().Result!.All(item => item.Id != qcId), "删没人用的角色");
    using (var db = XyzDb.Create(DbName))
    {
        Check(!db.Queryable<RolePermissionEntity>().Any(item => item.RoleId == qcId), "删角色连它的权限一起删");
    }

    deleted = account.DeleteRole(qcId);
    Check(!deleted.IsSuccess && deleted.ErrorMessage == ErrorCodes.AccountRoleNotFound, "再删一次：account.role_not_found");

    // 12. 删账号：内置 admin 不能删；删了登录不了、取会话回没有这个人
    changed = 0;
    var removed = account.DeleteUser(adminId);
    Check(!removed.IsSuccess && removed.ErrorMessage == ErrorCodes.AccountBuiltInUser, "删内置 admin：account.builtin_user");
    removed = account.DeleteUser(op01);
    Check(removed.IsSuccess && changed == 1, "删 op01，推一次");
    Check(account.Login("op01", "reset1").ErrorMessage == ErrorCodes.AccountLoginFailed, "删了的登录：account.login_failed");
    session = account.GetSession("op01");
    Check(!session.IsSuccess && session.ErrorMessage == ErrorCodes.AccountUserNotFound && session.Args.SequenceEqual(["op01"]), "删了的取会话：account.user_not_found");
    removed = account.DeleteUser(op01);
    Check(!removed.IsSuccess && removed.ErrorMessage == ErrorCodes.AccountUserNotFound, "再删一次：account.user_not_found");

    // 13. 三个服务：转给账号组件，数据转 DTO、错误码原样回
    var accountService = new AccountService();
    var response = await accountService.LoginAsync(new LoginRequest { UserName = "admin", Password = "admin" });
    Check(response.Success && response.DeserializeData<SessionDto>().RoleKind == RoleKind.Admin, "登录服务：回 SessionDto");
    response = await accountService.LoginAsync(new LoginRequest { UserName = "admin", Password = "x" });
    Check(!response.Success && response.Code == ErrorCodes.AccountLoginFailed, "登录服务：密码不对原样回错误码");
    response = await accountService.GetSessionAsync(new SessionRequest());
    Check(response.Success && response.DeserializeData<SessionDto>().RoleKind == RoleKind.Guest, "取会话服务：登录名空回访客");
    response = await accountService.ChangePasswordAsync(new PasswordChangeRequest { UserName = "admin", OldPassword = "admin", NewPassword = "admin2" });
    Check(response.Success && account.Login("admin", "admin2").IsSuccess, "改密码服务");
    response = await accountService.LogoutAsync(new LogoutRequest { UserName = "admin", IsIdle = true });
    Check(response.Success, "注销服务：只记日志");

    var userService = new UserService();
    response = await userService.CreateUserAsync(new UserCreateRequest { UserName = "eng01", DisplayName = "张工", RoleId = engineerRoleId, Password = "abcd" });
    Check(response.Success && response.DeserializeData<UserDto>().RoleName == "工程师", "用户服务建账号：回 UserDto");
    response = await userService.CreateUserAsync(new UserCreateRequest { UserName = "eng01", RoleId = engineerRoleId, Password = "abcd" });
    Check(!response.Success && response.Code == ErrorCodes.AccountUserExists && response.Args.SequenceEqual(["eng01"]), "用户服务：重名原样回错误码和参数");
    response = await userService.GetUsersAsync(new RpcRequest());
    Check(response.Success && response.DeserializeData<List<UserDto>>().Count == 3, "用户服务列账号");

    var roleService = new RoleService();
    response = await roleService.GetRolesAsync(new RpcRequest());
    Check(response.Success && response.DeserializeData<List<RoleDto>>().Count == 4, "角色服务列角色");
    response = await roleService.GetPermissionsAsync(new AccountIdRequest { Id = engineerRoleId });
    var engineerDto = response.DeserializeData<RolePermissionsDto>();
    Check(response.Success && engineerDto.RoleKind == RoleKind.Normal && engineerDto.Items.Single(item => item.Code == PermissionCodes.Io).Level == PermissionLevel.Operate,
        "角色服务取权限：回 RolePermissionsDto，档位经 JSON 来回不变");
    response = await roleService.DeleteRoleAsync(new AccountIdRequest { Id = adminRoleId });
    Check(!response.Success && response.Code == ErrorCodes.AccountBuiltInRole, "角色服务：删内置角色原样回错误码");

    // 14. 系统设置带登录开关：开着 True、关了 False、没配账号组件 False
    var settings = (await new SystemService().GetSettingsAsync(new RpcRequest())).DeserializeData<SystemSettingsDto>();
    Check(settings.IsLoginEnabled, "系统设置：Account 开着要登录");
    account.IsEnable = false;
    settings = (await new SystemService().GetSettingsAsync(new RpcRequest())).DeserializeData<SystemSettingsDto>();
    Check(!settings.IsLoginEnabled, "系统设置：Account 的 IsEnable 关了不要登录");
    account.IsEnable = true;

    SmokeIoc.Use<IAccount>(null);
    settings = (await new SystemService().GetSettingsAsync(new RpcRequest())).DeserializeData<SystemSettingsDto>();
    Check(!settings.IsLoginEnabled, "系统设置：没配 Account 节点不要登录");
    response = await new AccountService().LoginAsync(new LoginRequest { UserName = "admin", Password = "admin2" });
    Check(!response.Success && response.Code == ErrorCodes.AccountNotInstalled, "没配账号组件：登录服务回 account.not_installed");
    response = await new UserService().GetUsersAsync(new RpcRequest());
    Check(!response.Success && response.Code == ErrorCodes.AccountNotInstalled, "没配账号组件：用户服务回 account.not_installed");
    response = await new RoleService().GetRolesAsync(new RpcRequest());
    Check(!response.Success && response.Code == ErrorCodes.AccountNotInstalled, "没配账号组件：角色服务回 account.not_installed");

    // 15. 账号库建不起来：组件初始化回 false，登录、查询都回 account.failed
    XyzDb.Register("AccountSmokeBroken", $"DataSource={Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "no", "x.db")}", DbType.Sqlite);
    var broken = new AccountComponent { Database = "AccountSmokeBroken" };
    Check(!broken.InitComponent(), "账号库建不起来：组件初始化回 false（开机日志看得到）");
    login = broken.Login("admin", "admin");
    Check(!login.IsSuccess && login.ErrorMessage == ErrorCodes.AccountFailed, "账号库没准备好：登录回 account.failed");
    Check(broken.GetRoles().ErrorMessage == ErrorCodes.AccountFailed, "账号库没准备好：查角色回 account.failed");
}
finally
{
    try
    {
        SqliteCleanup(dbPath);
    }
    catch (IOException)
    {
        // 连接池还占着临时库，留在临时目录里
    }
}

Console.WriteLine($"PASS: {checks} account checks (账号组件、登录、会话、账号和角色的增删改、权限、推送、服务、系统设置)");

static Dictionary<string, PermissionLevel> PermissionsOf(IAccount account, long roleId)
{
    return account.GetPermissions(roleId).Result!.Items.ToDictionary(item => item.Code, item => item.Level);
}

static void SqliteCleanup(string path)
{
    if (File.Exists(path))
    {
        File.Delete(path);
    }
}

static ValueConfig Value(string name, string value)
{
    return new ValueConfig { Name = name, Value = value };
}

static ModuleConfig Node(string name, Type type, params ValueConfig[] values)
{
    return new ModuleConfig { Name = name, Type = type.FullName, Values = [.. values] };
}
