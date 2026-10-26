namespace xyz.Service;

/// <summary>
/// 操作日志拦截器
/// </summary>
public interface IActionFilter
{
    ActionCall? Begin(string method, string peer, string operatorName, object? request);

    /// <summary>
    /// 回包
    /// </summary>
    /// <param name="call"></param>
    /// <param name="response"></param>
    void End(ActionCall call, object? response);

    /// <summary>
    /// 异常：记 Error
    /// </summary>
    void Fault(ActionCall call, Exception exception);
}
