using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 腔体部件手动动作请求。code-first gRPC 的请求参数必须是消息类，故包一层。
/// </summary>
[ProtoContract]
public class ChamberPartActionRequest
{
    /// <summary>
    /// 模块实例名，如 "Chamber1"。
    /// </summary>
    [ProtoMember(1)]
    public string Module { get; set; } = string.Empty;

    /// <summary>
    /// 部件全路径（sc.xml 的组件路径，如 "Chamber1.Arm1.Lift"），就是部件状态推送里各部件的 Path。
    /// </summary>
    [ProtoMember(2)]
    public string Part { get; set; } = string.Empty;

    /// <summary>
    /// 要做的动作；部件不支持的动作后端回 chamber.part_action_unsupported。
    /// </summary>
    [ProtoMember(3)]
    public ChamberPartAction Action { get; set; }
}
