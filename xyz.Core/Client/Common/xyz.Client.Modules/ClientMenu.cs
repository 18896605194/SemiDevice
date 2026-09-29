namespace xyz.Client.Modules;

/// <summary>
/// 菜单声明：菜单写在代码里（平台的由壳声明，机型的由机型模块声明），不进数据库。
/// Code 同时是页面在 DI 里的 keyed 注册键；显示名按 Code 到语言包取 menu.{Code}，
/// 按模块生成的菜单（IO、腔体手动）直接显示模块名。
/// </summary>
public sealed class ClientMenu
{
    public ClientMenu(string? parentCode, string code, int sort, string? title = null)
    {
        ParentCode = parentCode;
        Code = code;
        Sort = sort;
        Title = title;
    }

    /// <summary>
    /// 父菜单 Code（如 "Manual"）；null 表示一级菜单。二级菜单挂的父菜单必须有人声明。
    /// </summary>
    public string? ParentCode { get; }

    /// <summary>
    /// 菜单编码，与 keyed UserControl 的注册键一致；显示名在语言包里配 menu.{Code}。
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// 排序号，越小越靠前。
    /// </summary>
    public int Sort { get; }

    /// <summary>
    /// 显示名。按模块生成的菜单填模块名（sc.xml 里的名字，如 Chamber1，不翻译）；
    /// null 时按 Code 到语言包取 menu.{Code}。
    /// </summary>
    public string? Title { get; }
}
