using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 一项 EC（在线可调参数）的定义与当前值：EC 设置页列参数、改值，通用输入框按 EcKey 取格式、上下限、单位都用它。
/// 数值按 ec.xml 原样给字符串，界面按不变区域性解析。
/// 值一变后端就推一条（EventBus，token = EventToken，不留存）：界面改的、组件自己写的都推。
/// </summary>
public class EcItemDto
{
    public const string EventToken = "Ec";

    /// <summary>
    /// 键："组件全路径.参数名"，如 LoadPort1.LoadTimeout、Chamber1.Door.ActionTimeoutMs。
    /// 层级跟 sc.xml 的组件层级一样，界面照它长出左边的组件树。
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 值格式：Int / Double / Bool / String / Enum。
    /// </summary>
    public string? Format { get; set; }

    public string? Min { get; set; }

    public string? Max { get; set; }

    public string? Unit { get; set; }

    public string? Default { get; set; }

    public string? Value { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// Enum 的可选值，逗号分隔。
    /// </summary>
    public string? Options { get; set; }
}

/// <summary>
/// 改一项 EC 的请求（EC 设置页）。code-first gRPC 的请求参数必须是消息类，故包一层。
/// </summary>
[ProtoContract]
public class EcSetRequest
{
    /// <summary>
    /// 键："组件全路径.参数名"。
    /// </summary>
    [ProtoMember(1)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 新值，按不变区域性写（小数点是点号）；布尔写 True / False，枚举写可选值里的一个。
    /// </summary>
    [ProtoMember(2)]
    public string Value { get; set; } = string.Empty;
}
