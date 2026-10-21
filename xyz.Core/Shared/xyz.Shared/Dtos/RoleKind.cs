namespace xyz.Shared.Dtos;

/// <summary>
/// 角色的种类
/// </summary>
public enum RoleKind
{
    /// <summary>
    /// 自己建的角色（预置的操作员、工程师也算）：能改名、能删、权限能改
    /// </summary>
    Normal = 0,

    /// <summary>
    /// 内置管理员：固定全部能操作，不能删、不能改
    /// </summary>
    Admin = 1,

    /// <summary>
    /// 内置访客：没登录的人按它算，不能删，权限能改，不能给账号用
    /// </summary>
    Guest = 2,
}
