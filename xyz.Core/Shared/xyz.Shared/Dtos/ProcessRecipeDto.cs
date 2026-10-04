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
/// 工艺配方字段的类型（sc.xml 字段表里的 Type）：整数、小数、下拉、开关、文本。
/// </summary>
public enum ProcessRecipeFieldType
{
    Int = 0,
    Double = 1,
    Choice = 2,
    Bool = 3,
    Text = 4,
}

/// <summary>
/// 一步：每个字段的值（字段名 → 值，都按文字传；有哪些字段由 sc.xml 的字段表定，见 <see cref="ProcessRecipeFieldDto"/>）。
/// 既是 <see cref="ProcessRecipeDto"/> 里的数据（走 JSON），也是保存请求里的消息（走 protobuf），所以带 ProtoContract。
/// </summary>
[ProtoContract]
public class ProcessRecipeStepDto
{
    [ProtoMember(1)]
    public Dictionary<string, string> Values { get; set; } = new();
}

/// <summary>
/// 编辑工艺配方用的字段表（sc.xml ProcessRecipe → Fields，一个字段一列）和合计时长的上限：界面按它生成步骤表，不写死字段。
/// </summary>
public class ProcessRecipeOptionsDto
{
    public List<ProcessRecipeFieldDto> Fields { get; set; } = [];

    /// <summary>
    /// 合计时长上限（秒）：腔体工艺超时里最短的那个；0 = 没有腔体，不限。
    /// </summary>
    public double MaxTotalSeconds { get; set; }
}

/// <summary>
/// 字段表里的一个字段（步骤表的一列）。下拉的选项已经按数据源取好：跟着别的字段走的（数据源写了 @字段名）按那个字段的值分开给；
/// 几个腔体装的不一样时，给的是所有腔体合起来的选项（用到具体腔体时后端再查）。
/// </summary>
public class ProcessRecipeFieldDto
{
    /// <summary>
    /// 字段名：存进配方文件、步骤里按它取值。
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 中文名（列名）。
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 英文名；空 = 英文界面也显示中文名。
    /// </summary>
    public string TextEn { get; set; } = string.Empty;

    public ProcessRecipeFieldType Type { get; set; }

    /// <summary>
    /// 单位（整数、小数才有），跟在列名后面。
    /// </summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// 下限；null = 不限。
    /// </summary>
    public double? Min { get; set; }

    /// <summary>
    /// 上限；null = 不限。
    /// </summary>
    public double? Max { get; set; }

    /// <summary>
    /// 最多几位小数（小数才有）；null = 不限。
    /// </summary>
    public int? Decimals { get; set; }

    /// <summary>
    /// 新加一步时填的值；老配方里没有这个字段时也按它补。
    /// </summary>
    public string Default { get; set; } = string.Empty;

    /// <summary>
    /// 必填：不勾的可以空着。
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// 选项跟着哪个字段走（数据源里 @ 后面的字段名）；空 = 不跟。
    /// </summary>
    public string ParentKey { get; set; } = string.Empty;

    /// <summary>
    /// 不跟别的字段走时的选项（下拉才有）。
    /// </summary>
    public List<string> Options { get; set; } = [];

    /// <summary>
    /// 跟着 <see cref="ParentKey"/> 走时：那个字段的值 → 能选的。
    /// </summary>
    public Dictionary<string, List<string>> OptionsByParent { get; set; } = new();
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
