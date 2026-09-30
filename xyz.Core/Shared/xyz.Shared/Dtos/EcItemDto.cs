namespace xyz.Shared.Dtos;

/// <summary>
/// 一项 EC（在线可调参数）的定义与当前值，界面取格式、上下限、单位用（如通用输入框按 EcKey 取范围）。
/// 数值按 ec.xml 原样给字符串，界面按不变区域性解析。
/// </summary>
public class EcItemDto
{
    /// <summary>
    /// 键："组件全路径.参数名"，如 LoadPort1.LoadTimeout、Chamber1.Door.ActionTimeoutMs。
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
