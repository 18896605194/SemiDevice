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
    /// 客户端据此生成按模块分的页面与菜单（IO 页一个一页）——sc.xml 里没配的模块，界面上就不该出现。
    /// sc.xml 配了安全信号（Safety 节点）时，它排在最前面，IO 页也给它一页。
    /// </summary>
    public List<string> Modules { get; set; } = [];

    /// <summary>
    /// 其中哪些是腔体（sc.xml 里装配出来的腔体模块名，如 Chamber1）。
    /// 客户端手动菜单下一个腔体一个子菜单——配了四个腔体就是四个，哪怕四个长得一模一样。
    /// </summary>
    public List<string> Chambers { get; set; } = [];

    /// <summary>
    /// 其中哪些是 LoadPort（按 sc.xml 的先后，如 LoadPort1、LoadPort2）。
    /// 主界面右栏一个 LoadPort 一个页签——sc.xml 里配几个就有几个。
    /// </summary>
    public List<string> LoadPorts { get; set; } = [];

    /// <summary>
    /// 其中哪些是机械手（按 sc.xml 的先后，如 Robot1）。
    /// 主界面默认的调度图一台机械手画一张（站点按它站点表里的方向摆）。
    /// </summary>
    public List<string> Robots { get; set; } = [];
}
