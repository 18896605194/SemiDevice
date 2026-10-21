using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 账号
/// </summary>
[ProtoContract]
public class UserDto
{
    [ProtoMember(1)]
    public long Id { get; set; }

    /// <summary>
    /// 登录名
    /// </summary>
    [ProtoMember(2)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// 显示名
    /// </summary>
    [ProtoMember(3)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 角色名（库里存的；内置角色界面按种类取语言包显示）
    /// </summary>
    [ProtoMember(4)]
    public string RoleName { get; set; } = string.Empty;

    [ProtoMember(5)]
    public bool IsEnabled { get; set; } = true;

    [ProtoMember(6)]
    public long RoleId { get; set; }

    [ProtoMember(7)]
    public RoleKind RoleKind { get; set; }

    /// <summary>
    /// 内置账号（admin）：不能删、不能停用、不能改角色
    /// </summary>
    [ProtoMember(8)]
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// 最后一次登录的时间；没登录过为空
    /// </summary>
    [ProtoMember(9)]
    public DateTime? LastLoginTime { get; set; }
}
