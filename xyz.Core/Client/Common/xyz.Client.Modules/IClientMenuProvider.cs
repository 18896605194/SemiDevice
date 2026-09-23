using System.Collections.Generic;

namespace xyz.Client.Modules;

/// <summary>
/// 菜单声明提供者：壳的平台菜单和机型模块的菜单都实现它，壳把所有提供者的菜单合成底部导航。
/// 机型模块实现它时由 ClientModuleLoader 自动注册进 DI，平台种子不用认识任何机型。
/// </summary>
public interface IClientMenuProvider
{
    /// <summary>
    /// 本提供者声明的菜单；同一个 Code 只认第一个声明的。
    /// </summary>
    IReadOnlyList<ClientMenu> Menus { get; }
}
