using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 用户契约对象。
/// </summary>
[ProtoContract]
public class UserDto
{
    [ProtoMember(1)]
    public long Id { get; set; }

    [ProtoMember(2)]
    public string UserName { get; set; } = string.Empty;

    [ProtoMember(3)]
    public string DisplayName { get; set; } = string.Empty;

    [ProtoMember(4)]
    public string RoleName { get; set; } = string.Empty;

    [ProtoMember(5)]
    public bool IsEnabled { get; set; } = true;
}
