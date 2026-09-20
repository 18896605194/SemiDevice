namespace xyz.Shared.Errors;


public static class ErrorCodes
{
    #region 模块通用

    /// <summary>模块不存在。Args: [模块名]</summary>
    public const string ModuleNotFound = "module.not_found";

    /// <summary>动作被拒（状态不允许或已有动作在途）。Args: [模块名, 当前状态码]</summary>
    public const string ActionRejected = "module.action_rejected";

    /// <summary>等待操作结果超时，最终结果尚未确认。Args: [操作名, 等待ms]</summary>
    public const string WaitTimeout = "module.wait_timeout";

    #endregion

    #region LoadPort

    /// <summary>指令被设备拒绝（未连接或在途）。Args: [操作名]</summary>
    public const string CommandRejected = "loadport.command_rejected";

    /// <summary>动作超时。Args: [操作名, 超时ms]</summary>
    public const string Timeout = "loadport.timeout";

    /// <summary>设备报错完成（ABS/NAK/协议错误）。Args: [操作名, 设备错误描述]</summary>
    public const string DeviceFailed = "loadport.device_failed";

    /// <summary>读码没发起（没挂读头、读头未连接或上一次还没读完）。Args: [模块名]</summary>
    public const string ReadCarrierIdRejected = "loadport.read_carrier_id_rejected";

    /// <summary>操作被 Abort 顶替。Args: [操作名]</summary>
    public const string Aborted = "module.action_aborted";

    /// <summary>操作步进内部异常。Args: [操作名, 异常消息]</summary>
    public const string OperationFaulted = "module.operation_faulted";

    #endregion

    #region Robot

    /// <summary>站点未在该机械手的站点表中配置。Args: [机械手模块名, 站点名]</summary>
    public const string StationNotFound = "robot.station_not_found";

    #endregion

    #region 搬运

    /// <summary>站点等不到可服务（一直没回到锚点态，或一直被别的机械手占着）。Args: [站点名, 等待ms]</summary>
    public const string StationBusy = "transfer.station_busy";

    /// <summary>站点准备被拒（状态不允许）。Args: [站点名, 准备阶段]</summary>
    public const string StationPrepareRejected = "transfer.station_prepare_rejected";

    /// <summary>站点准备失败。Args: [站点名, 准备阶段]</summary>
    public const string StationPrepareFailed = "transfer.station_prepare_failed";

    /// <summary>取放片发起被拒（机械手状态不允许、未连接、或站点未配置）。Args: [机械手模块名, 动作]</summary>
    public const string TransferRejected = "transfer.rejected";

    /// <summary>取放片失败。Args: [机械手模块名, 动作]</summary>
    public const string TransferFailed = "transfer.failed";

    /// <summary>环标记落不下去（站点状态跟搬运进度对不上，多半是被人工插手动了）。Args: [站点名, 标记]</summary>
    public const string TransferStepRejected = "transfer.step_rejected";

    #endregion

    #region 报警

    /// <summary>报警组件没装（sc.xml 没配 Alarm 节点）。</summary>
    public const string AlarmNotInstalled = "alarm.not_installed";

    /// <summary>这个来源没报过报警，没有可复位的。Args: [来源路径]</summary>
    public const string AlarmSourceNotFound = "alarm.source_not_found";

    #endregion

    #region 历史查询

    /// <summary>历史查询失败（读日志文件或数据库出错）。Args: [原因]</summary>
    public const string HistoryQueryFailed = "history.query_failed";

    #endregion
}
