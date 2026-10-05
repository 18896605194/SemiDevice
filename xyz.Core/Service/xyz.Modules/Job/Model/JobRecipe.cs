namespace xyz.Modules;

/// <summary>
/// 一个 PJ 用的配方快照：建 PJ 时从流程配方库、工艺配方库各取一份副本，之后库里改名、改内容、删掉都不影响这个 PJ。
/// 流程配方的第 1 步（从哪些 LoadPort 取）和最后一步（回哪些 LoadPort）单独记，中间要经过的站点在 <see cref="Steps"/>。
/// </summary>
public sealed class JobRecipe
{
    /// <summary>流程配方名（Host 的 RecID 就是它）。</summary>
    public required string SequenceName { get; init; }

    /// <summary>流程配方编号。</summary>
    public int SequenceIndex { get; init; }

    /// <summary>取快照时流程配方的版本。</summary>
    public int SequenceRevision { get; init; }

    /// <summary>第 1 步勾的 LoadPort：片只能从这些 LoadPort 来。</summary>
    public required IReadOnlyList<string> SourcePorts { get; init; }

    /// <summary>最后一步勾的 LoadPort：回片目标从这里定（源 LoadPort 在里面就回原槽，否则放到第一个勾的 LoadPort 同号槽）。</summary>
    public required IReadOnlyList<string> ReturnPorts { get; init; }

    /// <summary>中间要经过的站点，按顺序。</summary>
    public required IReadOnlyList<JobRouteStep> Steps { get; init; }
}

/// <summary>
/// 路线上的一步：可去的站点（哪个空去哪个，按 sc.xml 先后挑）和这一步的工艺配方快照（不用加工的站点没有）。
/// </summary>
public sealed class JobRouteStep
{
    /// <summary>站点分组名（sc.xml 原样，如 Chamber）。</summary>
    public required string Group { get; init; }

    /// <summary>可去的站点（模块名）。建 PJ 时已经去掉了跑不了这个配方的腔体。</summary>
    public required IReadOnlyList<string> Stations { get; init; }

    /// <summary>工艺配方名；空 = 这一步不加工（到了就算做完）。</summary>
    public string RecipeName { get; init; } = string.Empty;

    /// <summary>工艺配方快照；不加工、或没装工艺配方库时为 null。</summary>
    public ProcessRecipeData? Recipe { get; init; }

    /// <summary>这一步要不要加工。</summary>
    public bool NeedsProcess => RecipeName.Length > 0;
}
