using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 对腔体下某个设备做动作（轴回零 / 停止 / 复位 / 点动续，气缸升 / 降）的请求。code-first gRPC 的请求参数必须是消息类，故包一层。
/// </summary>
[ProtoContract]
public class ChamberDeviceRequest
{
    /// <summary>模块实例名，如 "Chamber1"。</summary>
    [ProtoMember(1)]
    public string Module { get; set; } = string.Empty;

    /// <summary>设备全路径（sc.xml 的组件路径，如 "Chamber1.Arm1"），就是设备推送里的 Path。</summary>
    [ProtoMember(2)]
    public string Device { get; set; } = string.Empty;
}

/// <summary>
/// 轴走到绝对位置。
/// </summary>
[ProtoContract]
public class ChamberAxisMoveRequest
{
    [ProtoMember(1)]
    public string Module { get; set; } = string.Empty;

    /// <summary>轴的全路径，如 "Chamber1.Arm1"。</summary>
    [ProtoMember(2)]
    public string Axis { get; set; } = string.Empty;

    /// <summary>目标位置（轴自己的单位）。</summary>
    [ProtoMember(3)]
    public double Position { get; set; }

    /// <summary>速度；0 = 按这根轴的 EC MoveSpeed。</summary>
    [ProtoMember(4)]
    public double Speed { get; set; }
}

/// <summary>
/// 轴走一段（步进）。
/// </summary>
[ProtoContract]
public class ChamberAxisStepRequest
{
    [ProtoMember(1)]
    public string Module { get; set; } = string.Empty;

    /// <summary>轴的全路径。</summary>
    [ProtoMember(2)]
    public string Axis { get; set; } = string.Empty;

    /// <summary>走多远，正负是方向；不能是 0。</summary>
    [ProtoMember(3)]
    public double Distance { get; set; }

    /// <summary>速度；0 = 按这根轴的 EC MoveSpeed。</summary>
    [ProtoMember(4)]
    public double Speed { get; set; }
}

/// <summary>
/// 轴点动（按住动、松开停）。
/// </summary>
[ProtoContract]
public class ChamberAxisJogRequest
{
    [ProtoMember(1)]
    public string Module { get; set; } = string.Empty;

    /// <summary>轴的全路径。</summary>
    [ProtoMember(2)]
    public string Axis { get; set; } = string.Empty;

    /// <summary>点动速度，正负是方向；不能是 0。</summary>
    [ProtoMember(3)]
    public double Speed { get; set; }
}
