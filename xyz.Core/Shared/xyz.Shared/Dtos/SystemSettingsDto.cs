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

    /// <summary>
    /// 这台设备装了哪些模块（sc.xml 里装配出来的模块名，如 LoadPort1、Chamber1）。
    /// 客户端据此生成按模块分的页面与菜单——sc.xml 里没配的模块，界面上就不该出现。
    /// </summary>
    public List<string> Modules { get; set; } = [];
}
