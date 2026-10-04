namespace xyz.Client.Modules;

/// <summary>
/// 页面里可以按机型换掉的一块（不是菜单页）的 keyed 注册键：平台先注册默认的，机型在自己的 IClientModule.Register 里
/// 用同一个键再注册一个 UserControl，后注册的生效——跟机型顶掉平台的整页（同一个菜单 Code）是一个路子。
/// </summary>
public static class ClientViewKeys
{
    /// <summary>
    /// 主界面中间的"整机调度"：平台默认按机械手站点表自动摆（一台机械手一张调度图，站点按 sc.xml 的方向摆在四周）；
    /// 机型要别的摆法（几台机械手、缓存位、对中台……）就注册自己的，主界面别的地方不用动。
    /// </summary>
    public const string MainDispatch = "Main.Dispatch";
}
