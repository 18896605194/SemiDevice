namespace xyz.Modules;

/// <summary>
/// 工艺配方能选的一条摆臂：sc.xml 里腔体下的摆臂轴名和挂在它下面的喷嘴的药液（Chemical），都原样给界面显示。
/// </summary>
public sealed class ProcessRecipeArm
{
    public ProcessRecipeArm(string name, IReadOnlyList<string> chemicals)
    {
        Name = name;
        Chemicals = chemicals;
    }

    public string Name { get; }

    /// <summary>
    /// 这条摆臂上的药液，按 sc.xml 里的先后。
    /// </summary>
    public IReadOnlyList<string> Chemicals { get; }
}
