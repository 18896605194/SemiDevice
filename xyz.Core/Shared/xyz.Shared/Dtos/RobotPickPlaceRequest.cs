using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// Robot 取放片请求。code-first gRPC 的请求参数必须是消息类（值类型不行），故包一层。
/// </summary>
[ProtoContract]
public class RobotPickPlaceRequest
{
    /// <summary>
    /// 模块实例名，如 "Robot1"。
    /// </summary>
    [ProtoMember(1)]
    public string Module { get; set; } = string.Empty;

    /// <summary>
    /// 手指号。
    /// </summary>
    [ProtoMember(2)]
    public int Arm { get; set; }

    /// <summary>
    /// 站点：站点表中的模块名，如 "LoadPort1"；站点号由该机械手的站点表解析。
    /// </summary>
    [ProtoMember(3)]
    public string Station { get; set; } = string.Empty;

    /// <summary>
    /// 层号（槽位号）。
    /// </summary>
    [ProtoMember(4)]
    public int Slot { get; set; }
}
