namespace xyz.Modules;

/// <summary>
/// 工艺是谁起的。
/// </summary>
public enum ProcessOrigin
{
    /// <summary>腔体手动页起的。</summary>
    Manual,

    /// <summary>Job 起的。</summary>
    Job,
}

/// <summary>
/// 一次起工艺的请求：做哪一片、用哪个工艺配方（带快照）、谁起的。
/// 配方给的是快照：起工艺时的内容就定下来了，库里之后改了、删了都不影响这一次。
/// </summary>
public sealed record ProcessRequest
{
    public ProcessOrigin Origin { get; init; } = ProcessOrigin.Manual;

    /// <summary>起工艺的 Job（PJ 名）；手动为空。</summary>
    public string? Owner { get; init; }

    /// <summary>要做的那一片（晶圆账内部标识）；空 = 腔里现在那一片（手动起工艺）。</summary>
    public Guid? WaferId { get; init; }

    /// <summary>腔体里的槽号（一般腔体只有 1 槽）。</summary>
    public int Slot { get; init; } = 1;

    /// <summary>Job 路线上第几步（手动为 -1）。</summary>
    public int Step { get; init; } = -1;

    /// <summary>工艺配方名。</summary>
    public required string RecipeName { get; init; }

    /// <summary>工艺配方快照；没装工艺配方库时为 null（只认名字）。</summary>
    public ProcessRecipeData? Recipe { get; init; }
}

/// <summary>
/// 起不了工艺的原因（错误码 + 参数，界面按码查语言包）。
/// </summary>
public sealed record ProcessRejection(string Code, IReadOnlyList<string> Args);
