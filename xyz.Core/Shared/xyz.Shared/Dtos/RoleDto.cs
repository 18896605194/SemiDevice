using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 角色契约对象。
/// </summary>
[ProtoContract]
public class RoleDto
{
    [ProtoMember(1)]
    public long Id { get; set; }

    [ProtoMember(2)]
    public string Name { get; set; } = string.Empty;

    [ProtoMember(3)]
    public string Description { get; set; } = string.Empty;

    [ProtoMember(4)]
    public int UserCount { get; set; }
}
