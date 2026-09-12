using System.Collections.Generic;

namespace xyz.Client.Modules;

/// <summary>
/// 机型模块可选实现的菜单声明：壳启动时把声明的菜单行补齐到后端菜单表，
/// 这样机型菜单由机型项目自己拥有，平台种子数据不用认识任何机型。
/// </summary>
public interface IClientMenuProvider
{
    /// <summary>
    /// 本机型需要的菜单行；为空表示不补菜单（页面只能由已有菜单 Code 命中）。
    /// </summary>
    IReadOnlyList<ClientMenu> Menus { get; }

    /// <summary>
    /// 需要从后端菜单表删掉的旧菜单 Code（机型改版后清理旧菜单项），默认不清理。
    /// </summary>
    IReadOnlyList<string> RetiredMenuCodes => Array.Empty<string>();
}
