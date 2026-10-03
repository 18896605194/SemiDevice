namespace xyz.Modules;

/// <summary>
/// 流程配方可选的一组站点：sc.xml 里的分组节点名（如 LoadPort、Chamber）和下面装的、能放片、机械手到得了的模块。
/// </summary>
public sealed class SequenceStationGroup
{
    public SequenceStationGroup(string name, IReadOnlyList<string> modules, bool isLoadPort, bool needsRecipe)
    {
        Name = name;
        Modules = modules;
        IsLoadPort = isLoadPort;
        NeedsRecipe = needsRecipe;
    }

    /// <summary>
    /// 分组名：sc.xml 里分组节点的 Name，原样给界面显示。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 模块名，按 sc.xml 里的先后。
    /// </summary>
    public IReadOnlyList<string> Modules { get; }

    /// <summary>
    /// 这一组全是 LoadPort：第 1 步、最后一步只能用它。
    /// </summary>
    public bool IsLoadPort { get; }

    /// <summary>
    /// 这一组有工艺腔：这一组的步骤要选工艺配方。
    /// </summary>
    public bool NeedsRecipe { get; }
}
