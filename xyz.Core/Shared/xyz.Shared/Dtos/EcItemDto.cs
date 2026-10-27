namespace xyz.Shared.Dtos;

/// <summary>
/// 一项 EC（在线可调参数）的定义与当前值：EC 设置页列参数、改值，通用输入框按 EcKey 取格式、上下限、单位都用它。
/// 数值按 ec.xml 原样给字符串，界面按不变区域性解析。
/// 值一变后端就推一条（RemoteEventBus，token = EventToken，不留存）：界面改的、元件自己写的都推。
/// </summary>
public class EcItemDto
{
    public const string EventToken = "Ec";

    /// <summary>
    /// 键："元件全路径.参数名"，如 LoadPort.LoadPort1.LoadTimeout、Chamber.Chamber1.Door.ActionTimeoutMs。
    /// 层级跟 machine.xml 的元件层级一样，界面照它长出左边的元件树。
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
