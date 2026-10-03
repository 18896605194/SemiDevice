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

    /// <summary>站点不许用这只手取放（sc.xml 机械手站点节点的 Arms）。Args: [机械手模块名, 站点名, 手指号]</summary>
    public const string ArmNotAllowed = "robot.arm_not_allowed";

    #endregion

    #region 腔体

    /// <summary>起工艺没给配方名。Args: [模块名]</summary>
    public const string RecipeRequired = "chamber.recipe_required";

    /// <summary>腔体下没有这个部件。Args: [模块名, 部件路径]</summary>
    public const string ChamberPartNotFound = "chamber.part_not_found";

    /// <summary>部件不支持这个动作（如对门发"去工艺位"）。Args: [部件路径, 动作]</summary>
    public const string ChamberPartActionUnsupported = "chamber.part_action_unsupported";

    /// <summary>部件指令没发出去：PLC 没连上、IO 点没配、轴没回零 / 没使能 / 正忙。Args: [部件路径, 动作]</summary>
    public const string ChamberPartCommandRejected = "chamber.part_command_rejected";

    /// <summary>部件动作没做成：到位超时、轴报错，或等过了 EC PartActionTimeout。Args: [部件路径, 动作]</summary>
    public const string ChamberPartActionFailed = "chamber.part_action_failed";

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

    #region IO

    /// <summary>IO 输出写不进（PLC 没连上、点表里没有这个点、写 PLC 出错）。Args: [类型 DO/AO, 点号]</summary>
    public const string IoWriteFailed = "io.write_failed";

    /// <summary>AO 下发值超出点表标定的工程量范围。Args: [点号, 下限, 上限, 单位]</summary>
    public const string IoOutOfRange = "io.out_of_range";

    #endregion

    #region EC

    /// <summary>EC 组件没装（sc.xml 没配 EC 节点）。</summary>
    public const string EcNotInstalled = "ec.not_installed";

    /// <summary>没有这一项 EC（组件树上没声明）。Args: [键]</summary>
    public const string EcNotFound = "ec.not_found";

    /// <summary>EC 值的写法不对。Args: [键, 格式 Int/Double/Bool/Enum]</summary>
    public const string EcInvalidFormat = "ec.invalid_format";

    /// <summary>EC 值超出声明的上下限。Args: [键, 下限, 上限, 单位]</summary>
    public const string EcOutOfRange = "ec.out_of_range";

    /// <summary>EC 值不在枚举的可选值里。Args: [键, 可选值]</summary>
    public const string EcInvalidOption = "ec.invalid_option";

    /// <summary>ec.xml 写不进去，值没改。Args: [键]</summary>
    public const string EcSaveFailed = "ec.save_failed";

    #endregion

    #region 晶圆账

    /// <summary>晶圆账没开（sc.xml 没配 WaferManager，或 IsEnable=False）。</summary>
    public const string WaferLedgerDisabled = "wafer.ledger_disabled";

    /// <summary>账上没有这个位置（模块没登记过槽位）。Args: [位置]</summary>
    public const string WaferLocationNotFound = "wafer.location_not_found";

    /// <summary>槽号超出这个位置的槽数。Args: [位置, 槽号, 槽数]</summary>
    public const string WaferSlotOutOfRange = "wafer.slot_out_of_range";

    /// <summary>这个槽上没片（可能刚被别处改过账）。Args: [位置, 槽号]</summary>
    public const string WaferNoWafer = "wafer.no_wafer";

    /// <summary>目标槽上已经有片。Args: [位置, 槽号, 片号]</summary>
    public const string WaferSlotOccupied = "wafer.slot_occupied";

    /// <summary>源和目标是同一个槽。</summary>
    public const string WaferSameSlot = "wafer.same_slot";

    /// <summary>补账没填片号。</summary>
    public const string WaferIdRequired = "wafer.id_required";

    /// <summary>补账的片号已经在账上。Args: [片号, 位置, 槽号]</summary>
    public const string WaferDuplicateId = "wafer.duplicate_id";

    #endregion

    #region 流程配方

    /// <summary>流程配方库没装（sc.xml 没配 Sequence 节点）。Args: []</summary>
    public const string SequenceNotInstalled = "sequence.not_installed";

    /// <summary>编号超出范围。Args: [编号, 个数]</summary>
    public const string SequenceIndexOutOfRange = "sequence.index_out_of_range";

    /// <summary>这个编号上没有流程配方（可能刚被别处删了）。Args: [编号]</summary>
    public const string SequenceNotFound = "sequence.not_found";

    /// <summary>新建的编号上已经有流程配方。Args: [编号, 已有的名称]</summary>
    public const string SequenceIndexOccupied = "sequence.index_occupied";

    /// <summary>名称没填。Args: []</summary>
    public const string SequenceNameRequired = "sequence.name_required";

    /// <summary>名称太长。Args: [最多几个字符]</summary>
    public const string SequenceNameTooLong = "sequence.name_too_long";

    /// <summary>名称里有不能用的字符（只能用字母、数字、_ 和 -）。Args: [名称]</summary>
    public const string SequenceNameInvalid = "sequence.name_invalid";

    /// <summary>名称跟别的编号重了（不分大小写）。Args: [名称, 那个编号]</summary>
    public const string SequenceNameDuplicate = "sequence.name_duplicate";

    /// <summary>保存时版本对不上：打开以后别处改过（另一台客户端保存或改名了）。Args: [编号]</summary>
    public const string SequenceRevisionMismatch = "sequence.revision_mismatch";

    /// <summary>步骤太少：第 1 步、最后一步是 LoadPort，中间至少要有一步。Args: []</summary>
    public const string SequenceTooFewSteps = "sequence.too_few_steps";

    /// <summary>第 1 步或最后一步不是 LoadPort 分组。Args: [步号]</summary>
    public const string SequenceStepNotLoadPort = "sequence.step_not_loadport";

    /// <summary>步骤的站点分组不在可选分组里（sc.xml 改过，或这个分组的站点机械手都到不了）。Args: [步号, 分组名]</summary>
    public const string SequenceGroupNotFound = "sequence.group_not_found";

    /// <summary>这一步一个站点都没勾。Args: [步号]</summary>
    public const string SequenceStationRequired = "sequence.station_required";

    /// <summary>勾的站点不在这一步的分组里。Args: [步号, 站点名, 分组名]</summary>
    public const string SequenceStationNotInGroup = "sequence.station_not_in_group";

    /// <summary>这一步要选工艺配方，没选。Args: [步号]</summary>
    public const string SequenceRecipeRequired = "sequence.recipe_required";

    /// <summary>流程配方文件写不进去或删不掉（内存里的没改）。Args: [编号, 原因]</summary>
    public const string SequenceSaveFailed = "sequence.save_failed";

    #endregion

    #region 历史查询

    /// <summary>历史查询失败（读日志文件或数据库出错）。Args: [原因]</summary>
    public const string HistoryQueryFailed = "history.query_failed";

    #endregion

    #region 数据曲线

    /// <summary>数据曲线没装（sc.xml 没配 DataChart 节点）。</summary>
    public const string DataChartNotInstalled = "datachart.not_installed";

    /// <summary>实时曲线没装（sc.xml 没配 RealChart 节点）。</summary>
    public const string RealChartNotInstalled = "realchart.not_installed";

    #endregion
}
