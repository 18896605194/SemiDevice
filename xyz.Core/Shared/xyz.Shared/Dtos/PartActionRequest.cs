using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 部件手动动作请求：对模块下某个部件调它标了 [ManualAction] 的方法。code-first gRPC 的请求参数必须是消息类，故包一层。
/// </summary>
[ProtoContract]
public class PartActionRequest
{
    /// <summary>模块实例名，如 "Chamber1"。</summary>
    [ProtoMember(1)]
    public string Module { get; set; } = string.Empty;

    /// <summary>部件全路径（sc.xml 的组件路径，如 "Chamber1.Arm1"），就是部件推送里各部件的 Path。</summary>
    [ProtoMember(2)]
    public string Part { get; set; } = string.Empty;

    /// <summary>动作名，就是组件上的方法名，如 "MoveTo"、"Jog"、"Stop"、"Open"。</summary>
    [ProtoMember(3)]
    public string Action { get; set; } = string.Empty;

    /// <summary>参数，按方法签名的先后、不变区域性写（小数点是点号）；可选参数可以不给。</summary>
    [ProtoMember(4)]
    public List<string> Args { get; set; } = [];
}
