using Microsoft.Extensions.DependencyInjection;

namespace xyz.Client.Modules;

/// <summary>
/// 客户端机型模块契约：机型项目只产出 DLL，由平台壳扫描并调用，
/// 壳不引用机型项目，机型项目之间也不互相引用。
/// </summary>
public interface IClientModule
{
    /// <summary>
    /// 把本机型的 ViewModel、页面（按菜单 Code 注册为 keyed UserControl）注册进 DI。
    /// </summary>
    void Register(IServiceCollection services);

    /// <summary>
    /// 机型界面资源程序集名（机型独有的控件、样式、语言包），没有就返回 null。
    /// 壳按当前界面语言合并其中的 Localization/Strings.{语言}.xaml；
    /// 框架提供的功能（含机型声明的框架菜单）的文字在平台语言包里，不经这里。
    /// </summary>
    string? PresentationAssembly => null;
}
