using xyz.Components.Attributes;

namespace xyz.Components.Components;

/// <summary>
/// 系统设置（sc.xml 的 System 节点）：界面语言这类全系统一份的配置。
/// </summary>
[Component(description: "系统设置（界面语言等）")]
public class SystemComponent : ComponentBase
{
    /// <summary>
    /// 当前系统设置；sc.xml 没装时为 null，各项用默认值。
    /// </summary>
    public static SystemComponent? Current { get; set; }

    public SystemComponent()
    {
        Current = this;
    }

    /// <summary>
    /// 界面语言：客户端启动时按它加载语言包（Strings.{Language}.xaml）。
    /// </summary>
    [SCEditor("zh-CN", "System", "界面语言：zh-CN 简体中文，en-US 英文；客户端启动时按它显示，改了重启客户端生效")]
    public string Language { get; set; } = "zh-CN";
}
