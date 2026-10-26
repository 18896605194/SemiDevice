namespace xyz.Shared.Dtos;

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
/// 一项权限：权限项编码 + 档位
/// </summary>
public class RolePermissionItem
{
    public string Code { get; set; } = string.Empty;

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
/// 账号、角色、权限变了的通知：登录着的客户端重新取自己的会话，用户、角色页重拉
/// </summary>
public class AccountChangedDto
{
    public const string EventToken = "Account";
}
