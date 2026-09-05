namespace xyz.Shared.Errors;


public static class ErrorCodes
{
    #region 模块通用

    /// <summary>模块不存在。Args: [模块名]</summary>
    public const string ModuleNotFound = "module.not_found";

    /// <summary>动作被拒（状态不允许或已有动作在途）。Args: [模块名, 当前状态码]</summary>
    public const string ActionRejected = "module.action_rejected";

    #endregion

    #region LoadPort

    /// <summary>指令被设备拒绝（未连接或在途）。Args: [操作名]</summary>
    public const string CommandRejected = "loadport.command_rejected";

    /// <summary>动作超时。Args: [操作名, 超时ms]</summary>
    public const string Timeout = "loadport.timeout";

    /// <summary>设备报错完成（ABS/NAK/协议错误）。Args: [操作名, 设备错误描述]</summary>
    public const string DeviceFailed = "loadport.device_failed";

    /// <summary>操作被 Abort 顶替。Args: [操作名]</summary>
    public const string Aborted = "module.action_aborted";

    /// <summary>操作步进内部异常。Args: [操作名, 异常消息]</summary>
    public const string OperationFaulted = "module.operation_faulted";

    #endregion
}
