using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 角色
/// </summary>
[ProtoContract]
public class RoleDto
{
    [ProtoMember(1)]
    public long Id { get; set; }

    /// <summary>
    /// 角色名（库里存的；内置角色界面按种类取语言包显示）
    /// </summary>
    [ProtoMember(2)]
    public string Name { get; set; } = string.Empty;

    [ProtoMember(3)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 用这个角色的账号数
    /// </summary>
    [ProtoMember(4)]
    public int UserCount { get; set; }

    [ProtoMember(5)]
    public RoleKind Kind { get; set; }
}
