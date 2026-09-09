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
}
