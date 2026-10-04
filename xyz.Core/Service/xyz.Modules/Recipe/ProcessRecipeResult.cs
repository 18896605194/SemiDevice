namespace xyz.Modules;

/// <summary>
/// 工艺配方库操作的结果：成了带上改完之后的副本；没成带错误码（xyz.Shared.Errors.ErrorCodes 里的码）和参数，服务层原样回给界面。
/// </summary>
public sealed class ProcessRecipeResult
{
    private ProcessRecipeResult(bool isOk, string code, IReadOnlyList<string> args, ProcessRecipeData? recipe)
    {
        IsOk = isOk;
        Code = code;
        Args = args;
        Recipe = recipe;
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
    /// 成了：操作后的那个工艺配方（副本）；删除、失败为 null。
    /// </summary>
    public ProcessRecipeData? Recipe { get; }

    public static ProcessRecipeResult Ok(ProcessRecipeData? recipe = null)
    {
        return new ProcessRecipeResult(true, string.Empty, [], recipe);
    }

    public static ProcessRecipeResult Fail(string code, params string[] args)
    {
        return new ProcessRecipeResult(false, code, args, null);
    }
}
