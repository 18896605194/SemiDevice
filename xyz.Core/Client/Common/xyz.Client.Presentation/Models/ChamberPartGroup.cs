namespace xyz.Client.Presentation.Models;

/// <summary>
/// 腔体手动页部件操作区的一组按钮：组名是部件在 sc.xml 里的路径（去掉腔体名，如 "Arm1.Lift"），按钮按部件种类给。
/// </summary>
public class ChamberPartGroup
{
    public ChamberPartGroup(string title, IReadOnlyList<ChamberPartActionItem> actions)
    {
        Title = title;
        Actions = actions;
    }

    /// <summary>组名：部件在 sc.xml 里的路径（去掉腔体名）。</summary>
    public string Title { get; }

    /// <summary>这一组的按钮。</summary>
    public IReadOnlyList<ChamberPartActionItem> Actions { get; }
}
