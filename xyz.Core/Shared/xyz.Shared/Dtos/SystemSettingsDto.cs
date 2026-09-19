namespace xyz.Shared.Dtos;

/// <summary>
/// 系统设置（后端 sc.xml 的 System 节点），客户端启动时拉一次。
/// </summary>
public class SystemSettingsDto
{
    /// <summary>
    /// 默认界面语言：简体中文。
    /// </summary>
    public const string DefaultLanguage = "zh-CN";

    /// <summary>
    /// 界面语言，如 zh-CN、en-US。
    /// </summary>
    public string Language { get; set; } = DefaultLanguage;
}
