using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 流程配方列表（配方 → 流程配方页左边）：编号 1~Capacity，只带用了的编号的名称。
/// 内容一变（新建、改名、删除、保存）后端就推一条 <see cref="SequenceChangedDto"/>（token = EventToken，不留存），界面收到后自己重拉。
/// </summary>
public class SequenceListDto
{
    public const string EventToken = "Sequence";

    /// <summary>
    /// 编号个数（sc.xml Sequence 节点的 Capacity）：界面按它列出 1~Capacity，不写死。
    /// </summary>
    public int Capacity { get; set; }

    /// <summary>
    /// 用了的编号，按编号从小到大。
    /// </summary>
    public List<SequenceSummaryDto> Items { get; set; } = [];
}

/// <summary>
/// 列表里的一项：编号和名称。
/// </summary>
public class SequenceSummaryDto
{
    public int Index { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 一个流程配方的全部内容：头信息 + 步骤。
/// 片在设备里怎么走：第 1 步从哪些 LoadPort 取片，中间按顺序经过哪些站点，最后一步放回哪些 LoadPort（默认从哪来回哪去）。
/// </summary>
public class SequenceDto
{
    public int Index { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public string ModifiedBy { get; set; } = string.Empty;

    public DateTime ModifiedAt { get; set; }

    /// <summary>
    /// 版本：新建是 1，每次保存、改名加 1。保存时把打开时的版本带回来，对不上说明别处改过。
    /// </summary>
    public int Revision { get; set; }

    public List<SequenceStepDto> Steps { get; set; } = [];
}

/// <summary>
/// 一步：站点分组（sc.xml 里的分组节点名，如 LoadPort、Chamber）、这一步可去的站点（勾几个 = 哪个空去哪个）、
/// 工艺配方（分组要的时候才有）。既是 <see cref="SequenceDto"/> 里的数据（走 JSON），也是保存请求里的消息（走 protobuf），所以带 ProtoContract。
/// </summary>
[ProtoContract]
public class SequenceStepDto
{
    [ProtoMember(1)]
    public string Group { get; set; } = string.Empty;

    [ProtoMember(2)]
    public List<string> Stations { get; set; } = [];

    [ProtoMember(3)]
    public string Recipe { get; set; } = string.Empty;
}

/// <summary>
/// 可选的站点分组：sc.xml 里的分组节点（不带 Type 的节点，如 LoadPort、Chamber）和下面装的模块，
/// 只列能放片、并且机械手站点表里配了的模块。界面原样显示，不翻译、不写死。
/// </summary>
public class SequenceStationGroupDto
{
    public string Name { get; set; } = string.Empty;

    public List<string> Modules { get; set; } = [];

    /// <summary>
    /// 这一组是 LoadPort：第 1 步、最后一步只能用它。
    /// </summary>
    public bool IsLoadPort { get; set; }

    /// <summary>
    /// 这一组的步骤要选工艺配方（工艺腔）。
    /// </summary>
    public bool NeedsRecipe { get; set; }
}

/// <summary>
/// 流程配方变了（新建、改名、删除、保存）：只带编号，界面收到后自己重拉。
/// </summary>
public class SequenceChangedDto
{
    public int Index { get; set; }
}

/// <summary>
/// 按编号取一个流程配方。
/// </summary>
[ProtoContract]
public class SequenceIndexRequest
{
    [ProtoMember(1)]
    public int Index { get; set; }
}

/// <summary>
/// 新建：在空编号上建一个，只给名称，内容按默认（LoadPort → 第一个要工艺配方的分组 → LoadPort，模块全勾）。
/// </summary>
[ProtoContract]
public class SequenceCreateRequest
{
    [ProtoMember(1)]
    public int Index { get; set; }

    [ProtoMember(2)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 操作人（客户端当前用户），记成创建人、修改人。
    /// </summary>
    [ProtoMember(3)]
    public string Operator { get; set; } = string.Empty;
}

/// <summary>
/// 重命名。
/// </summary>
[ProtoContract]
public class SequenceRenameRequest
{
    [ProtoMember(1)]
    public int Index { get; set; }

    [ProtoMember(2)]
    public string Name { get; set; } = string.Empty;

    [ProtoMember(3)]
    public string Operator { get; set; } = string.Empty;
}

/// <summary>
/// 删除。
/// </summary>
[ProtoContract]
public class SequenceDeleteRequest
{
    [ProtoMember(1)]
    public int Index { get; set; }

    /// <summary>
    /// 操作人，记进日志。
    /// </summary>
    [ProtoMember(2)]
    public string Operator { get; set; } = string.Empty;
}

/// <summary>
/// 保存说明和步骤（名称走重命名）。
/// </summary>
[ProtoContract]
public class SequenceSaveRequest
{
    [ProtoMember(1)]
    public int Index { get; set; }

    /// <summary>
    /// 打开时读到的版本；后端的版本已经变了就拒绝，免得两台客户端互相覆盖。
    /// </summary>
    [ProtoMember(2)]
    public int Revision { get; set; }

    [ProtoMember(3)]
    public string Description { get; set; } = string.Empty;

    [ProtoMember(4)]
    public List<SequenceStepDto> Steps { get; set; } = [];

    [ProtoMember(5)]
    public string Operator { get; set; } = string.Empty;
}
