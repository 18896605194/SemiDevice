namespace xyz.Shared.Dtos;

/// <summary>
/// 统一结果处理类
/// </summary>
public class HandleResult
{
    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage);

    public string ErrorMessage { get; set; } = string.Empty;

    public object? Result { get; set; }

    public string? AlarmCode { get; set; }

    public static HandleResult Success(object? result = null)
    {
        return new HandleResult { Result = result };
    }

    public static HandleResult Fail(string errorMessage)
    {
        return new HandleResult { ErrorMessage = errorMessage };
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

    public string ErrorMessage { get; set; } = string.Empty;

    public T? Result { get; set; }

    public static HandleResult<T> Success(T? result = default)
    {
        return new HandleResult<T> { Result = result };
    }

    public static HandleResult<T> Fail(string errorMessage)
    {
        return new HandleResult<T> { ErrorMessage = errorMessage };
    }
}
