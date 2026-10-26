namespace xyz.Service;

/// <summary>
/// 操作日志：一个调用该不该记、怎么记（宿主的 ActionFilter 每个调用调它）
/// </summary>
public interface IActionFilter
{
    /// <summary>
    /// 调用进来：要记的记一条，返回这次调用的记录，回包时用；不记的（Get / Query、订阅推送、上行发布）返回 null。
    /// method 是 gRPC 方法全名，peer 是来路（ipv4:IP:端口），operatorName 是请求头里的操作人
    /// </summary>
    ActionCall? Begin(string method, string peer, string operatorName, object? request);

    /// <summary>
    /// 回包：做成了记 Info，回的是失败（错误码 + 参数）记 Warn；推流的 response 为 null，做完就算成功
    /// </summary>
    void End(ActionCall call, object? response);

    /// <summary>
    /// 服务抛了异常：记 Error
    /// </summary>
    void Fault(ActionCall call, Exception exception);
}
