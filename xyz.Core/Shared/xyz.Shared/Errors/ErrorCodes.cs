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

    /// <summary>站点不支持这个任务（没有这种站内任务）。Args: [站点, 任务名]</summary>
    public const string StationTaskUnsupported = "module.task_unsupported";

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

    /// <summary>Load 回来的 Mapping 槽数跟 sc 配的槽数对不上，没落账。Args: [模块名, 设备回的槽数, sc 配的槽数]</summary>
    public const string SlotMapLengthMismatch = "loadport.slot_map_length_mismatch";

    /// <summary>Load 回来的 Mapping 有交叉片、叠片或认不出的槽。Args: [模块名, 槽号（逗号隔开）]</summary>
    public const string SlotMapAbnormal = "loadport.slot_map_abnormal";

    /// <summary>带 Mapping 的 Unload 扫到的跟晶圆账对不上（多片、少片，或交叉片、叠片、认不出）。Args: [模块名, 槽号（逗号隔开）]</summary>
    public const string UnloadSlotMapMismatch = "loadport.unload_slot_map_mismatch";

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

    /// <summary>起工艺给的配方不在工艺配方库里（可能被删了、改名了）。Args: [模块名, 配方名]</summary>
    public const string ChamberRecipeNotFound = "chamber.recipe_not_found";

    /// <summary>配方里下拉选的值这个腔体没有（几个腔体装的不一样时）。Args: [模块名, 配方名, 字段, 值]</summary>
    public const string ChamberRecipeOptionMissing = "chamber.recipe_option_missing";

    /// <summary>腔体下没有这个部件。Args: [模块名, 部件路径]</summary>
    public const string ChamberDeviceNotFound = "chamber.device_not_found";

    /// <summary>部件动作的参数不对：不是有限数、速度是负的、点动速度或步距是 0。Args: [部件路径, 动作（ChamberDeviceAction）]</summary>
    public const string ChamberDeviceArgsInvalid = "chamber.device_args_invalid";

    /// <summary>续点动时没有在按住的点动（已经松手、被停止或中止顶掉了）。Args: [部件路径, 动作（ChamberDeviceAction）]</summary>
    public const string ChamberJogNotHeld = "chamber.jog_not_held";

    /// <summary>部件指令没发出去：PLC 没连上、IO 点没配、轴没回零 / 没使能 / 正忙。Args: [部件路径, 动作（ChamberDeviceAction）]</summary>
    public const string ChamberDeviceCommandRejected = "chamber.device_command_rejected";

    /// <summary>部件动作没做成：到位超时、轴报错，或等过了 EC DeviceActionTimeout。Args: [部件路径, 动作（ChamberDeviceAction）]</summary>
    public const string ChamberDeviceActionFailed = "chamber.device_action_failed";

    /// <summary>整腔动作（回零、复位、中止、工艺）超过它的 EC 超时还没做完。Args: [模块名, 超时 ms]</summary>
    public const string ChamberActionTimeout = "chamber.action_timeout";

    /// <summary>起工艺拿不到配方步骤（没装工艺配方库，只给了名字），腔体不知道怎么做。Args: [模块名, 配方名]</summary>
    public const string ChamberRecipeStepsMissing = "chamber.recipe_steps_missing";

    /// <summary>起工艺时腔里不是要做的那一片（没片，或换过片）。Args: [模块名, 槽号]</summary>
    public const string ChamberWaferMismatch = "chamber.wafer_mismatch";

    /// <summary>腔里的片正在一个没结束的 Job 里，不能手动起工艺。Args: [模块名, 片号, Job 名]</summary>
    public const string ChamberWaferOwned = "chamber.wafer_owned";

    #endregion

    #region 搬运

    /// <summary>站点等不到可服务（一直没回到待命态，或一直被别的机械手占着）。Args: [站点名, 等待ms]</summary>
    public const string StationBusy = "transfer.station_busy";

    /// <summary>站点准备被拒（状态不允许）。Args: [站点名, 第几步准备（1 粗准备、2 开门放行）]</summary>
    public const string StationPrepareRejected = "transfer.station_prepare_rejected";

    /// <summary>站点准备失败。Args: [站点名, 第几步准备（1 粗准备、2 开门放行）]</summary>
    public const string StationPrepareFailed = "transfer.station_prepare_failed";

    /// <summary>取放片发起被拒（机械手状态不允许、未连接、或站点未配置）。Args: [机械手模块名, 动作]</summary>
    public const string TransferRejected = "transfer.rejected";

    /// <summary>取放片失败。Args: [机械手模块名, 动作]</summary>
    public const string TransferFailed = "transfer.failed";

    /// <summary>环标记落不下去（站点状态跟搬运进度对不上，多半是被人工插手动了）。Args: [站点名, 标记]</summary>
    public const string TransferStepRejected = "transfer.step_rejected";

    /// <summary>sc.xml 没配 Transfer 节点（没有搬运管理），切不了 Auto。Args: []</summary>
    public const string TransferNotInstalled = "transfer.not_installed";

    /// <summary>搬运管理停用了（sc.xml Transfer 节点 IsEnable=False），切不了 Auto、启动不了搬运操作。Args: []</summary>
    public const string TransferDisabled = "transfer.disabled";

    /// <summary>搬运参数里的站点不在搬运模块表里（没装、或不是能放片的站点）。Args: [站点名]</summary>
    public const string TransferStationNotFound = "transfer.station_not_found";

    /// <summary>搬运参数里的槽号超出站点的槽数。Args: [站点名, 槽号, 槽数]</summary>
    public const string TransferSlotOutOfRange = "transfer.slot_out_of_range";

    /// <summary>源和目标是同一个槽。Args: []</summary>
    public const string TransferSameSlot = "transfer.same_slot";

    /// <summary>源槽上的片不是要搬的那一片（被人换过、重新 Mapping 过）。Args: [站点名, 槽号, 槽上现在的片号]</summary>
    public const string TransferWaferMismatch = "transfer.wafer_mismatch";

    /// <summary>这片正被一个没结束的 Job 占着，手动搬不了。Args: [片号, Job 名]</summary>
    public const string TransferWaferOwned = "transfer.wafer_owned";

    /// <summary>这个槽（或槽上的片）已被别的搬运操作占用。Args: [站点名, 槽号]</summary>
    public const string TransferSlotLocked = "transfer.slot_locked";

    /// <summary>没有一台机械手两个站点都到得了（站点表里没配）。Args: [源站点, 目标站点]</summary>
    public const string TransferNoRobot = "transfer.no_robot";

    /// <summary>点名的这只手用不了：两个站点不都许用、手上有片，或正被别的操作占用。Args: [机械手模块名, 手指号]</summary>
    public const string TransferArmUnavailable = "transfer.arm_unavailable";

    /// <summary>机械手没有一只能用的手（都有片、都被占着，或站点不许用）。Args: [机械手模块名]</summary>
    public const string TransferNoArm = "transfer.no_arm";

    /// <summary>这片晶圆没有搬运失败保留的资源（已经释放，或标识不对）。Args: [晶圆内部标识]</summary>
    public const string TransferNotHeld = "transfer.not_held";

    /// <summary>手动传片等结果超时：搬运还在跑，结果待确认。Args: [操作名称, 等待ms]</summary>
    public const string TransferWaitTimeout = "transfer.wait_timeout";

    #endregion

    #region Job（SEMI E94 CJ / E40 PJ）

    /// <summary>sc.xml 没配 Job 节点（没有 Job 管理）。Args: []</summary>
    public const string JobNotInstalled = "job.not_installed";

    /// <summary>Job 管理停用了（sc.xml Job 节点 IsEnable=False）。Args: []</summary>
    public const string JobDisabled = "job.disabled";

    /// <summary>命令等 Job 管理受理超时，命令可能稍后还会执行。Args: [等待ms]</summary>
    public const string JobCommandTimeout = "job.command_timeout";

    /// <summary>没有这个 Job（CJ 或 PJ）。Args: [Job 名]</summary>
    public const string JobNotFound = "job.not_found";

    /// <summary>当前状态不收这个命令（照 SEMI E94 / E40 的转换表）。Args: [Job 名, 命令, 当前状态]</summary>
    public const string JobCommandNotAllowed = "job.command_not_allowed";

    /// <summary>Job 正在停止或中止，不再收这个命令。Args: [Job 名, 命令]</summary>
    public const string JobEnding = "job.ending";

    /// <summary>启动要在 Auto 下。Args: []</summary>
    public const string JobNotAuto = "job.not_auto";

    /// <summary>Job 名不合规（1~80 个 ASCII 可见字符或空格，不能有 ? * ~ &gt; :）。Args: [名字]</summary>
    public const string JobIdInvalid = "job.id_invalid";

    /// <summary>Job 名已经在用（没结束的 Job 里有同名的）。Args: [名字]</summary>
    public const string JobIdDuplicate = "job.id_duplicate";

    /// <summary>没有这个 LoadPort。Args: [LoadPort]</summary>
    public const string JobLoadPortNotFound = "job.loadport_not_found";

    /// <summary>LoadPort 上没有能取片的载具（没放、没 Load、没 Mapping）。Args: [LoadPort]</summary>
    public const string JobCarrierNotReady = "job.carrier_not_ready";

    /// <summary>这个 LoadPort 上已经有没结束的 Job。Args: [LoadPort, Job 名]</summary>
    public const string JobLoadPortBusy = "job.loadport_busy";

    /// <summary>没选要做的片（没有槽配了流程配方）。Args: [LoadPort]</summary>
    public const string JobNoWafers = "job.no_wafers";

    /// <summary>这一槽没片。Args: [LoadPort, 槽号]</summary>
    public const string JobSlotEmpty = "job.slot_empty";

    /// <summary>这片不能做（交叉片、叠片这类）。Args: [LoadPort, 槽号, 片号, 物理状态]</summary>
    public const string JobWaferNotNormal = "job.wafer_not_normal";

    /// <summary>这片已经做过（工艺状态不是待处理）。Args: [LoadPort, 槽号, 片号, 工艺状态]</summary>
    public const string JobWaferProcessed = "job.wafer_processed";

    /// <summary>这片已经在别的没结束的 Job 里。Args: [片号, Job 名]</summary>
    public const string JobWaferOwned = "job.wafer_owned";

    /// <summary>流程配方库里没有这个流程配方。Args: [流程配方名]</summary>
    public const string JobSequenceNotFound = "job.sequence_not_found";

    /// <summary>流程配方的第 1 步没勾这个 LoadPort（片不能从这儿取）。Args: [流程配方名, LoadPort]</summary>
    public const string JobSequenceSourceMismatch = "job.sequence_source_mismatch";

    /// <summary>流程配方最后一步没有能回片的 LoadPort。Args: [流程配方名]</summary>
    public const string JobSequenceNoReturn = "job.sequence_no_return";

    /// <summary>回片的槽用不了（那个 LoadPort 没载具、槽上有片，或被别的 Job 占着）。Args: [LoadPort, 槽号]</summary>
    public const string JobReturnSlotUnavailable = "job.return_slot_unavailable";

    /// <summary>流程配方这一步一个能去的站点都没有（没装、停用、机械手到不了，或跑不了这一步的工艺配方）。Args: [流程配方名, 第几步, 工艺配方名]</summary>
    public const string JobStepNoStation = "job.step_no_station";

    /// <summary>工艺配方库里没有流程配方引用的工艺配方。Args: [流程配方名, 工艺配方名]</summary>
    public const string JobRecipeNotFound = "job.recipe_not_found";

    /// <summary>建 PJ 时找不到这个载具（不在任何 LoadPort 上）。Args: [载具号]</summary>
    public const string JobCarrierNotFound = "job.carrier_not_found";

    /// <summary>建 CJ 时引用的 PJ 不存在，或已经归了别的 CJ。Args: [PJ 名]</summary>
    public const string JobProcessJobUnavailable = "job.process_job_unavailable";

    /// <summary>建 PJ 时这个载具上要做的片已经归了别的没结束的 PJ（槽号重了，或者其中一个没给槽号 = 整个载具）。Args: [载具号, PJ 名]</summary>
    public const string JobSlotClaimed = "job.slot_claimed";

    /// <summary>建 CJ 时这个载具已经有没删的 CJ（料还没到、按载具号认的时候查）。Args: [载具号, CJ 名]</summary>
    public const string JobCarrierBusy = "job.carrier_busy";

    /// <summary>流程配方这一步用到的站点不支持要做的任务（比如这一步要做工艺，组里有个站点不能做工艺）。Args: [流程配方名, 第几步, 站点, 任务名]</summary>
    public const string JobStationTaskUnsupported = "job.station_task_unsupported";

    /// <summary>没有这个任务（PJ 里没有这个来源槽的片，或任务序号不对）。Args: [PJ 名, 来源槽, 第几个任务]</summary>
    public const string JobTaskNotFound = "job.task_not_found";

    /// <summary>这个任务没出错，不用人工处理。Args: [PJ 名, 片号, 第几个任务]</summary>
    public const string JobTaskNotError = "job.task_not_error";

    /// <summary>标记完成不了：片在账上不在这一步做完该在的地方。Args: [片号, 任务名, 账上的位置]</summary>
    public const string JobTaskPositionMismatch = "job.task_position_mismatch";

    /// <summary>任务出错的原因：片不在该在的地方（被改了账、载具被拿走、重新 Mapping 过）。Args: [片号, 该在的位置]</summary>
    public const string JobWaferMoved = "job.wafer_moved";

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

    /// <summary>这一步选的工艺配方不在工艺配方库里（可能被删了、改名了）。Args: [步号, 配方名]</summary>
    public const string SequenceRecipeNotFound = "sequence.recipe_not_found";

    /// <summary>这一步勾的腔体上没有工艺配方里下拉选的值（几个腔体装的不一样时）。Args: [步号, 腔体, 配方名, 字段, 值]</summary>
    public const string SequenceRecipeOptionMissing = "sequence.recipe_option_missing";

    /// <summary>流程配方文件写不进去或删不掉（内存里的没改）。Args: [编号, 原因]</summary>
    public const string SequenceSaveFailed = "sequence.save_failed";

    /// <summary>Host 下的新流程配方放不下：编号都用完了。Args: [个数]</summary>
    public const string SequenceFull = "sequence.full";

    /// <summary>按名字找不到流程配方（Host 删、取的时候）。Args: [名称]</summary>
    public const string SequenceNameNotFound = "sequence.name_not_found";

    /// <summary>Host 下的流程配方内容读不出来（不是这边给出去的那种 JSON）。Args: [名称]</summary>
    public const string SequenceBodyInvalid = "sequence.body_invalid";

    #endregion

    #region 工艺配方

    /// <summary>工艺配方库没装（sc.xml 没配 ProcessRecipe 节点）。Args: []</summary>
    public const string ProcessRecipeNotInstalled = "process_recipe.not_installed";

    /// <summary>编号超出范围。Args: [编号, 个数]</summary>
    public const string ProcessRecipeIndexOutOfRange = "process_recipe.index_out_of_range";

    /// <summary>这个编号上没有工艺配方（可能刚被别处删了）。Args: [编号]</summary>
    public const string ProcessRecipeNotFound = "process_recipe.not_found";

    /// <summary>新建的编号上已经有工艺配方。Args: [编号, 已有的名称]</summary>
    public const string ProcessRecipeIndexOccupied = "process_recipe.index_occupied";

    /// <summary>名称没填。Args: []</summary>
    public const string ProcessRecipeNameRequired = "process_recipe.name_required";

    /// <summary>名称太长。Args: [最多几个字符]</summary>
    public const string ProcessRecipeNameTooLong = "process_recipe.name_too_long";

    /// <summary>名称里有不能用的字符（只能用字母、数字、_ 和 -）。Args: [名称]</summary>
    public const string ProcessRecipeNameInvalid = "process_recipe.name_invalid";

    /// <summary>名称跟别的编号重了（不分大小写）。Args: [名称, 那个编号]</summary>
    public const string ProcessRecipeNameDuplicate = "process_recipe.name_duplicate";

    /// <summary>保存时版本对不上：打开以后别处改过（另一台客户端保存或改名了）。Args: [编号]</summary>
    public const string ProcessRecipeRevisionMismatch = "process_recipe.revision_mismatch";

    /// <summary>一步都没有。Args: []</summary>
    public const string ProcessRecipeNoSteps = "process_recipe.no_steps";

    /// <summary>必填的字段没填。Args: [步号, 字段]</summary>
    public const string ProcessRecipeValueRequired = "process_recipe.value_required";

    /// <summary>整数字段填的不是整数。Args: [步号, 字段, 填的]</summary>
    public const string ProcessRecipeValueNotInteger = "process_recipe.value_not_integer";

    /// <summary>小数字段填的不是数字。Args: [步号, 字段, 填的]</summary>
    public const string ProcessRecipeValueNotNumber = "process_recipe.value_not_number";

    /// <summary>比下限小。Args: [步号, 字段, 下限, 单位（带前导空格，没有就空）]</summary>
    public const string ProcessRecipeValueBelowMin = "process_recipe.value_below_min";

    /// <summary>比上限大。Args: [步号, 字段, 上限, 单位（带前导空格，没有就空）]</summary>
    public const string ProcessRecipeValueAboveMax = "process_recipe.value_above_max";

    /// <summary>小数位比字段表里配的多。Args: [步号, 字段, 最多几位]</summary>
    public const string ProcessRecipeValueTooPrecise = "process_recipe.value_too_precise";

    /// <summary>下拉、开关的值不在能选的里面（数据源变了，或别处传来的值不对）。Args: [步号, 字段, 值]</summary>
    public const string ProcessRecipeValueNotInOptions = "process_recipe.value_not_in_options";

    /// <summary>合计时长超过腔体的工艺超时（腔体会在做完之前就判超时）。Args: [合计秒数, 上限秒数]</summary>
    public const string ProcessRecipeTotalTooLong = "process_recipe.total_too_long";

    /// <summary>工艺配方文件写不进去或删不掉（内存里的没改）。Args: [编号, 原因]</summary>
    public const string ProcessRecipeSaveFailed = "process_recipe.save_failed";

    /// <summary>Host 下的新工艺配方放不下：编号都用完了。Args: [个数]</summary>
    public const string ProcessRecipeFull = "process_recipe.full";

    /// <summary>按名字找不到工艺配方（Host 删、取的时候）。Args: [名称]</summary>
    public const string ProcessRecipeNameNotFound = "process_recipe.name_not_found";

    /// <summary>Host 下的工艺配方内容读不出来（不是这边给出去的那种 JSON）。Args: [名称]</summary>
    public const string ProcessRecipeBodyInvalid = "process_recipe.body_invalid";

    #endregion

    #region 配方管理（Host 远程管配方）

    /// <summary>ON-LINE REMOTE 时配方只能由 Host 改（sc.xml Eap.Recipe 的 LockLocalEditInRemote 开着），本地新建、改名、保存、删除都拒。Args: []</summary>
    public const string RecipeLockedByHost = "recipe.locked_by_host";

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
