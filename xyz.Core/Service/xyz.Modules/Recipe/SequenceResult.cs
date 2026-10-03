namespace xyz.Modules;

/// <summary>
/// 流程配方库操作的结果：成了带上改完之后的副本；没成带错误码（xyz.Shared.Errors.ErrorCodes 里的码）和参数，服务层原样回给界面。
/// </summary>
public sealed class SequenceResult
{
    private SequenceResult(bool isOk, string code, IReadOnlyList<string> args, SequenceData? sequence)
    {
        IsOk = isOk;
        Code = code;
        Args = args;
        Sequence = sequence;
    }

    public bool IsOk { get; }

    /// <summary>
    /// 错误码；成了是空的。
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// 错误码参数，顺序见错误码的注释。
    /// </summary>
    public IReadOnlyList<string> Args { get; }

    /// <summary>
    /// 成了：操作后的那个流程配方（副本）；删除、失败为 null。
    /// </summary>
    public SequenceData? Sequence { get; }

    public static SequenceResult Ok(SequenceData? sequence = null)
    {
        return new SequenceResult(true, string.Empty, [], sequence);
    }

    public static SequenceResult Fail(string code, params string[] args)
    {
        return new SequenceResult(false, code, args, null);
    }
}
