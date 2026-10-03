namespace xyz.Client.Recipe.Models;

/// <summary>
/// 路线预览里一个框的种类：样式按它换底色和图标。
/// </summary>
public enum RouteBoxKind
{
    /// <summary>LoadPort（取片、放片）。</summary>
    LoadPort,

    /// <summary>要选工艺配方的站点（工艺腔）。</summary>
    Process,

    /// <summary>别的站点（Aligner、Buffer……）。</summary>
    Other,

    /// <summary>这一步一个都没勾：红框。</summary>
    Missing,
}
