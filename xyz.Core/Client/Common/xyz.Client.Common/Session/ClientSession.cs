using xyz.Shared.Dtos;

namespace xyz.Client.Common.Session;

/// <summary>
/// 当前会话：谁在用、什么角色、每一项权限几档；在界面线程上改，变了发通知
/// </summary>
public static class ClientSession
{
    /// <summary>
    /// 没启用登录时记操作人用的名字（跟以前一样）
    /// </summary>
    public const string DefaultUserName = "Admin";

    /// <summary>
    /// 访客记操作人用的名字
    /// </summary>
    public const string GuestUserName = "Guest";

    private static SessionDto _current = new() { RoleKind = RoleKind.Guest };

    /// <summary>
    /// 要不要登录（后端 sc.xml 的 Account 节点）；不要登录时谁都全部能操作
    /// </summary>
    public static bool IsLoginEnabled { get; private set; }

    /// <summary>
    /// 登录了没有（访客、没启用登录都算没登录）
    /// </summary>
    public static bool IsLoggedIn => IsLoginEnabled && _current.UserName.Length > 0;

    /// <summary>
    /// 全部能操作：没启用登录，或者是管理员
    /// </summary>
    public static bool IsAdmin => !IsLoginEnabled || _current.RoleKind == RoleKind.Admin;

    /// <summary>
    /// 当前角色的种类（访客、管理员、自己建的）
    /// </summary>
    public static RoleKind RoleKind => _current.RoleKind;

    /// <summary>
    /// 当前角色名（库里存的；内置角色界面按种类取语言包显示）
    /// </summary>
    public static string RoleName => _current.RoleName;

    /// <summary>
    /// 当前登录名；访客、没启用登录为空
    /// </summary>
    public static string LoginName => IsLoggedIn ? _current.UserName : string.Empty;

    /// <summary>
    /// 当前显示名；访客、没启用登录为空
    /// </summary>
    public static string DisplayName => IsLoggedIn ? _current.DisplayName : string.Empty;

    /// <summary>
    /// 记操作人用的名字（账单调整、配方的创建人修改人、建 Job 都从这儿取）
    /// </summary>
    public static string UserName
    {
        get
        {
            if (!IsLoginEnabled)
            {
                return DefaultUserName;
            }

            return IsLoggedIn ? _current.UserName : GuestUserName;
        }
    }

    /// <summary>
    /// 键盘鼠标多少分钟没动就自动注销；0 = 不自动注销
    /// </summary>
    public static int IdleLogoutMinutes => _current.IdleLogoutMinutes;

    /// <summary>
    /// 会话变了（登录、注销、权限变了、开关定下来）
    /// </summary>
    public static event Action? Changed;

    /// <summary>
    /// 定下要不要登录（启动时问后端；后端不在线时先当要登录、什么都只看，连上以后再定）
    /// </summary>
    public static void SetLoginEnabled(bool isLoginEnabled)
    {
        if (IsLoginEnabled == isLoginEnabled)
        {
            return;
        }

        IsLoginEnabled = isLoginEnabled;
        Changed?.Invoke();
    }

    /// <summary>
    /// 换成这个会话（登录成功、注销回访客、刷新）
    /// </summary>
    public static void Apply(SessionDto session)
    {
        _current = session;
        Changed?.Invoke();
    }

    /// <summary>
    /// 这一项现在几档：没启用登录、管理员全部能操作；这个角色没配过的按"只看"
    /// </summary>
    public static PermissionLevel LevelOf(string code)
    {
        if (IsAdmin)
        {
            return PermissionLevel.Operate;
        }

        return _current.Permissions.TryGetValue(code, out var level) ? level : PermissionLevel.View;
    }

    /// <summary>
    /// 这一项能不能操作（功能项就是允不允许）
    /// </summary>
    public static bool CanOperate(string code)
    {
        return LevelOf(code) == PermissionLevel.Operate;
    }
}
