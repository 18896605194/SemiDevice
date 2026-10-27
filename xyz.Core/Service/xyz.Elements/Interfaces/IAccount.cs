using xyz.Shared.Dtos;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 账号：登录、用户、角色和各菜单的权限
/// </summary>
public interface IAccount
{
    /// <summary>
    /// 要不要登录（machine.xml）：False 时客户端不登录、不分权限
    /// </summary>
    bool IsEnable { get; }

    /// <summary>
    /// 登录：核对登录名和密码，成功回会话
    /// </summary>
    HandleResult<SessionDto> Login(string userName, string password);

    /// <summary>
    /// 取会话：登录名为空取访客的
    /// </summary>
    HandleResult<SessionDto> GetSession(string userName);

    /// <summary>
    /// 记一笔注销（会话在客户端，这里只记日志）
    /// </summary>
    void NoteLogout(string userName, bool isIdle);

    /// <summary>
    /// 改自己的密码（要旧密码）
    /// </summary>
    HandleResult ChangePassword(string userName, string oldPassword, string newPassword);

    /// <summary>
    /// 全部账号
    /// </summary>
    HandleResult<IReadOnlyList<UserDto>> GetUsers();

    /// <summary>
    /// 新建账号
    /// </summary>
    HandleResult<UserDto> CreateUser(string userName, string displayName, long roleId, string password);

    /// <summary>
    /// 改账号的显示名、角色
    /// </summary>
    HandleResult UpdateUser(long id, string displayName, long roleId);

    /// <summary>
    /// 重置密码（不要旧密码）
    /// </summary>
    HandleResult ResetPassword(long id, string password);

    /// <summary>
    /// 停用 / 启用账号
    /// </summary>
    HandleResult SetUserEnabled(long id, bool isEnabled);

    /// <summary>
    /// 删账号
    /// </summary>
    HandleResult DeleteUser(long id);

    /// <summary>
    /// 全部角色：管理员在前，访客在后
    /// </summary>
    HandleResult<IReadOnlyList<RoleDto>> GetRoles();

    /// <summary>
    /// 新建角色
    /// </summary>
    HandleResult<RoleDto> CreateRole(string name, string description);

    /// <summary>
    /// 改角色名和说明
    /// </summary>
    HandleResult UpdateRole(long id, string name, string description);

    /// <summary>
    /// 删角色
    /// </summary>
    HandleResult DeleteRole(long id);

    /// <summary>
    /// 一个角色配过的权限
    /// </summary>
    HandleResult<RolePermissionsDto> GetPermissions(long roleId);

    /// <summary>
    /// 存一个角色的权限：整张换掉
    /// </summary>
    HandleResult SavePermissions(long roleId, IReadOnlyList<RolePermissionItem> items);
}
