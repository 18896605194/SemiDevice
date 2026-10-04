using ProtoBuf;

namespace xyz.Shared.Dtos;

/// <summary>
/// 工艺配方列表（配方 → 工艺配方页左边，也是流程配方页、腔体手动页选工艺配方的弹窗里列的）：编号 1~Capacity，只带用了的编号。
/// 内容一变（新建、改名、删除、保存）后端就推一条 <see cref="ProcessRecipeChangedDto"/>（token = EventToken，不留存），界面收到后自己重拉。
/// </summary>
public class ProcessRecipeListDto
{
    public const string EventToken = "ProcessRecipe";

    /// <summary>
    /// 编号个数（sc.xml ProcessRecipe 节点的 Capacity）：界面按它列出 1~Capacity，不写死。
    /// </summary>
    public int Capacity { get; set; }

    /// <summary>
    /// 用了的编号，按编号从小到大。
    /// </summary>
    public List<ProcessRecipeSummaryDto> Items { get; set; } = [];
}

/// <summary>
/// 列表里的一项：编号、名称，选择弹窗里还要说明和合计时长。
/// </summary>
public class ProcessRecipeSummaryDto
{
    public int Index { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 合计时长（各步时间加起来），秒。
    /// </summary>
    public double TotalSeconds { get; set; }
}

/// <summary>
/// 一个工艺配方的全部内容：头信息 + 步骤。工艺配方说的是片进了腔体以后怎么做：一步一步转多快、喷什么、喷在哪。
/// </summary>
public class ProcessRecipeDto
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

    public List<ProcessRecipeStepDto> Steps { get; set; } = [];
}

/// <summary>
/// 摆臂怎么喷：Time = 停在一个位置，按这一步的时间喷；Scan = 在两个位置之间来回扫，扫到这一步时间到。
/// 界面上就显示 Time / Scan（用户定的叫法）。
/// </summary>
public enum ProcessArmMode
{
    Time = 0,
    Scan = 1,
}

/// <summary>
/// 一步：时间、转速；摆臂（空 = 这一步不出液，摆臂在 Home）、药液（这条摆臂上喷嘴的 Chemical）、流量、方式和位置。
/// 位置是晶圆坐标：0 = 从 Home 摆过去先到的晶圆边缘，150 = 晶圆中心。
/// 既是 <see cref="ProcessRecipeDto"/> 里的数据（走 JSON），也是保存请求里的消息（走 protobuf），所以带 ProtoContract。
/// </summary>
[ProtoContract]
public class ProcessRecipeStepDto
{
    /// <summary>
    /// 这一步多长，秒。
    /// </summary>
    [ProtoMember(1)]
    public double Seconds { get; set; }

    /// <summary>
    /// 转速 rpm（0 = 停着泡）。
    /// </summary>
    [ProtoMember(2)]
    public int Rpm { get; set; }

    /// <summary>
    /// 摆臂名（sc.xml 里腔体下的摆臂轴，原样）；空 = 不出液，摆臂在 Home。
    /// </summary>
    [ProtoMember(3)]
    public string Arm { get; set; } = string.Empty;

    /// <summary>
    /// 药液（这条摆臂上喷嘴的 Chemical，原样）；不出液时为空。
    /// </summary>
    [ProtoMember(4)]
    public string Chemical { get; set; } = string.Empty;

    /// <summary>
    /// 流量 L/min；不出液时为 0。
    /// </summary>
    [ProtoMember(5)]
    public double Flow { get; set; }

    [ProtoMember(6)]
    public ProcessArmMode Mode { get; set; }

    /// <summary>
    /// 位置（晶圆坐标）：Time 停在这里喷；Scan 从这里扫到 <see cref="ScanTo"/>。
    /// </summary>
    [ProtoMember(7)]
    public double Position { get; set; }

    /// <summary>
    /// Scan 的另一头（晶圆坐标）；Time 时为 0。
    /// </summary>
    [ProtoMember(8)]
    public double ScanTo { get; set; }

    /// <summary>
    /// Scan 的速度 mm/s；Time 时为 0。
    /// </summary>
    [ProtoMember(9)]
    public double ScanSpeed { get; set; }
}

/// <summary>
/// 编辑工艺配方能选什么、范围多少：摆臂和它上面的药液来自腔体下装的摆臂轴和喷嘴（sc.xml），范围来自 sc.xml 的 ProcessRecipe 节点，
/// 合计时长的上限是腔体的工艺超时（EC）。界面拿它出下拉框、给输入框定范围，不写死。
/// </summary>
public class ProcessRecipeOptionsDto
{
    public List<ProcessArmDto> Arms { get; set; } = [];

    public double MinSeconds { get; set; }

    public double MaxSeconds { get; set; }

    public int MaxRpm { get; set; }

    public double MinFlow { get; set; }

    public double MaxFlow { get; set; }

    /// <summary>
    /// 位置下限：晶圆边缘（0）。
    /// </summary>
    public double MinPosition { get; set; }

    /// <summary>
    /// 位置上限：晶圆中心（150）。
    /// </summary>
    public double MaxPosition { get; set; }

    public double MinScanSpeed { get; set; }

    public double MaxScanSpeed { get; set; }

    /// <summary>
    /// 合计时长上限（秒）：腔体工艺超时里最短的那个；0 = 没有腔体，不限。
    /// </summary>
    public double MaxTotalSeconds { get; set; }

    /// <summary>
    /// 页面上"添加"的那一步的时间（跟新建工艺配方时的第一步一样，后端定）。
    /// </summary>
    public double NewStepSeconds { get; set; }

    public int NewStepRpm { get; set; }
}

/// <summary>
/// 一条摆臂：名字（sc.xml 里的摆臂轴名）和它上面喷嘴的药液（Chemical），都原样显示。
/// </summary>
public class ProcessArmDto
{
    public string Name { get; set; } = string.Empty;

    public List<string> Chemicals { get; set; } = [];
}

/// <summary>
/// 工艺配方变了（新建、改名、删除、保存）：只带编号，界面收到后自己重拉。
/// </summary>
public class ProcessRecipeChangedDto
{
    public int Index { get; set; }
}

/// <summary>
/// 按编号取一个工艺配方。
/// </summary>
[ProtoContract]
public class ProcessRecipeIndexRequest
{
    [ProtoMember(1)]
    public int Index { get; set; }
}

/// <summary>
/// 新建：在空编号上建一个，只给名称，内容按默认（一步，不出液）。
/// </summary>
[ProtoContract]
public class ProcessRecipeCreateRequest
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
public class ProcessRecipeRenameRequest
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
public class ProcessRecipeDeleteRequest
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
public class ProcessRecipeSaveRequest
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
    public List<ProcessRecipeStepDto> Steps { get; set; } = [];

    [ProtoMember(5)]
    public string Operator { get; set; } = string.Empty;
}
