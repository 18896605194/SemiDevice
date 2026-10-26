namespace xyz.Shared.Dtos;

/// <summary>
/// 角色
/// </summary>
public class RoleDto
{
    public long Id { get; set; }

    /// <summary>
    /// 角色名（库里存的；内置角色界面按种类取语言包显示）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 用这个角色的账号数
    /// </summary>
    public int UserCount { get; set; }

    public RoleKind Kind { get; set; }
}
