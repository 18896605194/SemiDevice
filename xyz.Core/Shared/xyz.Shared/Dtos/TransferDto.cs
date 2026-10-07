using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 手动传片请求：把源站点某槽的片搬到目标站点某槽。站点名就是模块名（sc.xml 原样），槽号从 1 开始。
/// 机械手、手臂不填（空 / 0）由搬运管理挑：第一台两边都到得了的机械手、第一只空着且两边都许用的手。
/// </summary>
[ProtoContract]
public class TransferRequestDto
{
    [ProtoMember(1)]
    public string Source { get; set; } = string.Empty;

    [ProtoMember(2)]
    public int SourceSlot { get; set; }

    [ProtoMember(3)]
    public string Target { get; set; } = string.Empty;

    [ProtoMember(4)]
    public int TargetSlot { get; set; }

    /// <summary>机械手模块名；空 = 自动挑。</summary>
    [ProtoMember(5)]
    public string Robot { get; set; } = string.Empty;

    /// <summary>手指号；0 = 自动挑。</summary>
    [ProtoMember(6)]
    public int Arm { get; set; }
}

/// <summary>
/// 按晶圆标识放开搬运失败保留的资源（人到现场确认片位、对好账之后）。
/// </summary>
[ProtoContract]
public class TransferReleaseRequest
{
    [ProtoMember(1)]
    public Guid WaferId { get; set; }
}

/// <summary>
/// 搬完的一趟（手动传片成功时回给界面，走 JSON）：用了哪台机械手、哪只手。
/// </summary>
public class TransferDoneDto
{
    public string Robot { get; set; } = string.Empty;

    public int Arm { get; set; }

    public string Source { get; set; } = string.Empty;

    public int SourceSlot { get; set; }

    public string Target { get; set; } = string.Empty;

    public int TargetSlot { get; set; }
}
