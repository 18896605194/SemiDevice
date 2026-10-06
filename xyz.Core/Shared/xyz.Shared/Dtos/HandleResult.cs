namespace xyz.Shared.Dtos;

/// <summary>
/// 统一结果处理类
/// </summary>
public class HandleResult
{
    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);

    /// <summary>失败的原因；按框架的错误码规矩用时放错误码（ErrorCodes 里的），界面按码查语言包。成功为空。</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>错误码的参数（顺序见错误码的注释）；成功或不带参数为空。</summary>
    public IReadOnlyList<string> Args { get; set; } = [];

    public object? Result { get; set; }

    public string? AlarmCode { get; set; }

    public static HandleResult Success(object? result = null)
    {
        return new HandleResult { Result = result };
    }

    public static HandleResult Fail(string errorMessage, params string[] args)
    {
        return new HandleResult { ErrorMessage = errorMessage, Args = args };
    }

    public static HandleResult Fail(HandleResult result, string errorMessage)
    {
        result.ErrorMessage = errorMessage;
        return result;
    }
}


public class HandleResult<T>
{
    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);

    /// <summary>失败的原因；按框架的错误码规矩用时放错误码（ErrorCodes 里的），界面按码查语言包。成功为空。</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>错误码的参数（顺序见错误码的注释）；成功或不带参数为空。</summary>
    public IReadOnlyList<string> Args { get; set; } = [];

    public T? Result { get; set; }

    public static HandleResult<T> Success(T? result = default)
    {
        return new HandleResult<T> { Result = result };
    }

    public static HandleResult<T> Fail(string errorMessage, params string[] args)
    {
        return new HandleResult<T> { ErrorMessage = errorMessage, Args = args };
    }
}
