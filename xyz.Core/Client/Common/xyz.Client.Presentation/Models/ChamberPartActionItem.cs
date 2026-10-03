using xyz.Shared.Dtos;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 部件操作区的一个按钮：对哪个部件（全路径）做什么动作，按钮上写什么；整个对象当命令参数交给页面的 ViewModel。
/// </summary>
public class ChamberPartActionItem
{
    public ChamberPartActionItem(string part, string path, ChamberPartAction action, string text)
    {
        Part = part;
        Path = path;
        Action = action;
        Text = text;
    }

    /// <summary>部件在 sc.xml 里的路径（去掉腔体名），失败提示里用。</summary>
    public string Part { get; }

    /// <summary>部件全路径，发给后端找部件。</summary>
    public string Path { get; }

    /// <summary>动作。</summary>
    public ChamberPartAction Action { get; }

    /// <summary>按钮上的字（按当前语言）。</summary>
    public string Text { get; }
}
