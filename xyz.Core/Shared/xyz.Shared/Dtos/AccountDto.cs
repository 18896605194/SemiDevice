using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 登录请求
/// </summary>
[ProtoContract]
public class LoginRequest
{
    [ProtoMember(1)]
    public string UserName { get; set; } = string.Empty;

    [ProtoMember(2)]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// 取会话的请求：登录名为空取访客的
/// </summary>
[ProtoContract]
public class SessionRequest
{
    [ProtoMember(1)]
    public string UserName { get; set; } = string.Empty;
}

/// <summary>
/// 注销请求：会话在客户端，后端只记一行日志
/// </summary>
[ProtoContract]
public class LogoutRequest
{
    [ProtoMember(1)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 是不是闲置自动注销的
    /// </summary>
    [ProtoMember(2)]
    public bool IsIdle { get; set; }
}

/// <summary>
/// 改自己密码的请求
/// </summary>
[ProtoContract]
public class PasswordChangeRequest
{
    [ProtoMember(1)]
    public string UserName { get; set; } = string.Empty;

    [ProtoMember(2)]
    public string OldPassword { get; set; } = string.Empty;

    [ProtoMember(3)]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// 会话：这个人是谁、什么角色、每一项权限几档（访客也是一个会话，登录名为空）
/// </summary>
public class SessionDto
{
    /// <summary>
    /// 登录名；访客为空
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 显示名；没填就是登录名
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 角色名（库里存的；内置角色界面按种类取语言包显示）
    /// </summary>
    public string RoleName { get; set; } = string.Empty;

    public RoleKind RoleKind { get; set; }

    /// <summary>
    /// 这个角色配过的各项档位：权限项编码 → 档位。没配过的不在里面，界面按"只看"算；管理员是空的（全部能操作）
    /// </summary>
    public Dictionary<string, PermissionLevel> Permissions { get; set; } = [];

    /// <summary>
    /// 键盘鼠标多少分钟没动就自动注销（账号组件的 EC）；0 = 不自动注销
    /// </summary>
    public int IdleLogoutMinutes { get; set; }
}

/// <summary>
/// 新建账号的请求
/// </summary>
[ProtoContract]
public class UserCreateRequest
{
    [ProtoMember(1)]
    public string UserName { get; set; } = string.Empty;

    [ProtoMember(2)]
    public string DisplayName { get; set; } = string.Empty;

    [ProtoMember(3)]
    public long RoleId { get; set; }

    [ProtoMember(4)]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// 改账号的请求：显示名、角色
/// </summary>
[ProtoContract]
public class UserUpdateRequest
{
    [ProtoMember(1)]
    public long Id { get; set; }

    [ProtoMember(2)]
    public string DisplayName { get; set; } = string.Empty;

    [ProtoMember(3)]
    public long RoleId { get; set; }
}

/// <summary>
/// 管理员重置别人密码的请求（不用旧密码）
/// </summary>
[ProtoContract]
public class UserPasswordResetRequest
{
    [ProtoMember(1)]
    public long Id { get; set; }

    [ProtoMember(2)]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// 停用 / 启用账号的请求
/// </summary>
[ProtoContract]
public class UserEnableRequest
{
    [ProtoMember(1)]
    public long Id { get; set; }

    [ProtoMember(2)]
    public bool IsEnabled { get; set; }
}

/// <summary>
/// 按编号指一个账号或角色的请求（删账号、删角色、取角色的权限）
/// </summary>
[ProtoContract]
public class AccountIdRequest
{
    [ProtoMember(1)]
    public long Id { get; set; }
}

/// <summary>
/// 新建角色、改角色名和说明的请求；新建时编号不用填
/// </summary>
[ProtoContract]
public class RoleSaveRequest
{
    [ProtoMember(1)]
    public long Id { get; set; }

    [ProtoMember(2)]
    public string Name { get; set; } = string.Empty;

    [ProtoMember(3)]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 一项权限：权限项编码 + 档位
/// </summary>
[ProtoContract]
public class RolePermissionItem
{
    [ProtoMember(1)]
    public string Code { get; set; } = string.Empty;

    [ProtoMember(2)]
    public PermissionLevel Level { get; set; }
}

/// <summary>
/// 一个角色配过的权限
/// </summary>
public class RolePermissionsDto
{
    public long RoleId { get; set; }

    public RoleKind RoleKind { get; set; }

    public List<RolePermissionItem> Items { get; set; } = [];
}

/// <summary>
/// 存一个角色权限的请求：整张换掉
/// </summary>
[ProtoContract]
public class RolePermissionsSaveRequest
{
    [ProtoMember(1)]
    public long RoleId { get; set; }

    [ProtoMember(2)]
    public List<RolePermissionItem> Items { get; set; } = [];
}

/// <summary>
/// 账号、角色、权限变了的通知：登录着的客户端重新取自己的会话，用户、角色页重拉
/// </summary>
public class AccountChangedDto
{
    public const string EventToken = "Account";
}
