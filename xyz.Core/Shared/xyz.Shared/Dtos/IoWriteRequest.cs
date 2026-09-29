using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// IO 输出点写入请求（IO 界面上手动开关 DO、下发 AO）。code-first gRPC 的请求参数必须是消息类，故包一层。
/// </summary>
[ProtoContract]
public class IoWriteRequest
{
    /// <summary>
    /// 点号（点表 Index，即 PLC 数组下标）。
    /// </summary>
    [ProtoMember(1)]
    public int Index { get; set; }

    /// <summary>
    /// 写入值：DO 非 0 = ON、0 = OFF；AO 是工程值（按点表标定反算成原始码再下发）。
    /// </summary>
    [ProtoMember(2)]
    public double Value { get; set; }
}
