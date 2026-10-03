namespace xyz.Client.Recipe.Models;

/// <summary>
/// 路线预览里的一个框：一个站点（或"没勾"提示）。
/// </summary>
public sealed class RouteBoxModel
{
    public RouteBoxModel(string text, RouteBoxKind kind)
    {
        Text = text;
        Kind = kind;
    }

    public string Text { get; }

    public RouteBoxKind Kind { get; }
}
