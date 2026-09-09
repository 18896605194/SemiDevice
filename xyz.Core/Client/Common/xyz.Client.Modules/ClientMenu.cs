namespace xyz.Client.Modules;

/// <summary>
/// 机型菜单声明：按 Code 挂到已有父菜单下，Code 同时也是页面在 DI 里的 keyed 注册键。
/// </summary>
public sealed class ClientMenu
{
    public ClientMenu(string parentCode, string name, string code, int sort)
    {
        ParentCode = parentCode;
        Name = name;
        Code = code;
        Sort = sort;
    }

    /// <summary>
    /// 父菜单 Code（如 "Manual"），必须是后端菜单表里已存在的菜单。
    /// </summary>
    public string ParentCode { get; }

    /// <summary>
    /// 菜单显示名。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 菜单编码，与 keyed UserControl 的注册键一致。
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// 排序号，越小越靠前。
    /// </summary>
    public int Sort { get; }
}
