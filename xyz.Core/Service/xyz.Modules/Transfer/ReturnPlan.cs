namespace xyz.Modules;

/// <summary>
/// 全部回片的计划：机内（机械手手上、腔体和别的站点上）每一片回哪——回它的来源 LoadPort 同号槽；回不去的写原因。
/// 机械手手上的排在前面：手空出来，腔体里的才有手去取。
/// </summary>
public sealed record ReturnPlan(IReadOnlyList<ReturnMove> Moves, IReadOnlyList<ReturnSkip> Skipped)
{
    /// <summary>空计划（没装晶圆账、账停用）。</summary>
    public static ReturnPlan Empty { get; } = new([], []);
}

/// <summary>一片怎么回：从机内的位置（源是机械手时槽号是手指号）回来源 LoadPort 的槽。</summary>
public sealed record ReturnMove(Guid WaferId, string WaferName, string Source, int SourceSlot, bool SourceIsArm, string Target, int TargetSlot);

/// <summary>回不去的一片和原因（错误码 + 参数，界面按语言包写成句子），要人工处理。</summary>
public sealed record ReturnSkip(Guid WaferId, string WaferName, string Source, int SourceSlot, bool SourceIsArm, string Code,
    IReadOnlyList<string> Args)
{
    public static ReturnSkip Of(ReturnMove move, string code, IReadOnlyList<string> args)
    {
        return new ReturnSkip(move.WaferId, move.WaferName, move.Source, move.SourceSlot, move.SourceIsArm, code, args);
    }
}
