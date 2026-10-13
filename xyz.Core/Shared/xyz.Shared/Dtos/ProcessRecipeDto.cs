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
/// 一个工艺配方的全部内容：配方头 + 步骤。工艺配方说的是片进了腔体以后怎么做：一步一步转多快、喷什么、喷在哪。
/// </summary>
public class ProcessRecipeDto
{
    public int Index { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 这份配方给哪种站点用（sc.xml 里站点配的 Type）：只能在 Type 一样的腔体上跑，步骤里下拉的选项也按它取。
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 超时时间（秒，按文字传，跟字段值一样）：腔体跑这份配方的上限，合计时长不能超过它。
    /// </summary>
    public string Timeout { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 配方头里自己在字段表加的那些项：字段名 → 值（固定的几项在上面各自的属性里）。
    /// </summary>
    public Dictionary<string, string> Head { get; set; } = new();

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
/// 编辑工艺配方用的两张字段表：配方头（sc.xml ProcessRecipe → Head，一项一行）和步骤（ProcessRecipe → Fields，一个字段一列），
/// 界面按它们生成配方头和步骤表，不写死字段。
/// </summary>
public class ProcessRecipeOptionsDto
{
    /// <summary>
    /// 配方头的项，按先后。
    /// </summary>
    public List<ProcessRecipeFieldDto> HeadFields { get; set; } = [];

    /// <summary>
    /// 步骤的列，按先后。
    /// </summary>
    public List<ProcessRecipeFieldDto> Fields { get; set; } = [];
}

/// <summary>
/// 字段表里的一个字段：步骤表的一列，或配方头的一项。下拉的选项就是 sc.xml 字段表里写的：
/// 步骤的按站点的 Type 分开写（配方头选了哪种 Type 用哪一套），配方头的只有一套（Type 那一项的选项是 sc 里站点配的几种 Type）。
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
    /// 配方头下拉的选项（配方头的下拉才有），按先后。
    /// </summary>
    public List<string> Options { get; set; } = [];

    /// <summary>
    /// 步骤下拉的选项（步骤的下拉才有）：站点的 Type → 选项，按 sc.xml 里写的先后；站点没配 Type 的那一套键是空字符串。
    /// </summary>
    public Dictionary<string, List<string>> OptionsByType { get; set; } = new();

    /// <summary>
    /// 配方头里程序算的那几项（编号、名称、创建人……）：页面上只显示、不能改。
    /// </summary>
    public bool IsAuto { get; set; }
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
/// 保存配方头里能填的项和步骤（名称走重命名）。
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

    [ProtoMember(6)]
    public string Type { get; set; } = string.Empty;

    [ProtoMember(7)]
    public string Timeout { get; set; } = string.Empty;

    /// <summary>
    /// 配方头里自己在字段表加的那些项：字段名 → 值。
    /// </summary>
    [ProtoMember(8)]
    public Dictionary<string, string> Head { get; set; } = new();
}
