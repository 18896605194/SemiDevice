using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 菜单契约对象。
/// </summary>
[ProtoContract]
public class MenuDto
{
    [ProtoMember(1)]
    public long Id { get; set; }

    [ProtoMember(2)]
    public string Name { get; set; } = string.Empty;

    [ProtoMember(3)]
    public string Code { get; set; } = string.Empty;

    [ProtoMember(4)]
    public long? ParentId { get; set; }

    [ProtoMember(5)]
    public int Sort { get; set; }

    [ProtoMember(6)]
    public bool IsEnabled { get; set; } = true;
}
