using System.Text;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;
using xyz.Shared.Dtos;

namespace xyz.Components.Components;

/// <summary>
/// 配方管理（SEMI E30 工艺程序管理，sc.xml 的 Eap 下的 Recipe 节点）：Host 的 S7 翻成流程配方库（ISequenceComponent）、
/// 工艺配方库（IProcessRecipeComponent）各自的命令（跟本地两个配方页同一套检查），两个库报上来的"变了"翻成事件。
/// 流程配方、工艺配方各在自己的库里，这里分开处理、不混在一起：Host 那边的配方号（PPID）就是名字，先看流程配方库有没有，再看工艺配方库
/// （两个库的名字不会重）；Host 下一个两个库都还没有的名字，看 JSON 的样子由库自己认（流程配方每一步是分组 + 站点，工艺配方每一步是字段名 → 值）。
/// 内容（PPBODY）是 JSON，发出去用 B（UTF-8），收的时候 A、B 都认。
/// ① S7F1 问能不能下 → S7F2 PPGNT；② S7F3 下（没有就新建、有就覆盖）→ S7F4 ACKC7；③ S7F5 要 → S7F6；
/// ④ S7F17 删（空表 = 两个库全删）→ S7F18 ACKC7；⑤ S7F19 列 → S7F20。下、删要 ON-LINE REMOTE；要、列在 ON-LINE 就行（离线由 E30 挡掉）。
/// SC LockLocalEditInRemote：ON-LINE REMOTE 时锁住本地两个配方页（配方服务改之前问 <see cref="IsLocalEditLocked"/>）。
/// </summary>
[Component(description: "配方管理（SEMI E30 工艺程序管理）：S7 下、取、删、列流程配方和工艺配方，变了报事件")]
public class E30RecipeComponent : ComponentBase, IE30Callback
{
    /// <summary>PPGNT：可以下。</summary>
    private const byte GrantOk = 0;

    /// <summary>PPGNT：不收（不在 ON-LINE REMOTE）。</summary>
    private const byte GrantWillNotAccept = 5;

    /// <summary>ACKC7：收下了。</summary>
    private const byte Accepted = 0;

    /// <summary>ACKC7：不让做（不在 ON-LINE REMOTE，或看不出是流程配方还是工艺配方、库没过检查、删不掉）。</summary>
    private const byte PermissionNotGranted = 1;

    /// <summary>ACKC7：没有这个配方（删的时候）。</summary>
    private const byte IdNotFound = 4;

    /// <summary>Host 改的时候记的操作人。</summary>
    private const string HostOperator = "Host";

    #region DV、事件

    private const string DvChangeName = "PPChangeName";
    private const string DvChangeStatus = "PPChangeStatus";

    [DataVariable(ValueFormat.String, "变了的流程配方或工艺配方（名字）")]
    public readonly string ChangeNameData = DvChangeName;

    [DataVariable(ValueFormat.Int, "怎么变的：1 新建、2 修改、3 删除")]
    public readonly string ChangeStatusData = DvChangeStatus;

    [EventAttribut("流程配方或工艺配方变了（新建、修改、删除；本地配方页和 Host 改的都报）", Data = new[] { DvChangeName, DvChangeStatus })]
    public readonly string ProcessProgramChange = "ProcessProgramChange";

    #endregion

    private E30Component? _gem;
    private ISequenceComponent? _sequences;
    private IProcessRecipeComponent? _processRecipes;

    /// <summary>当前配方管理组件；Eap 下没配 Recipe 节点时为 null（本地配方页就不会被锁）。</summary>
    public static E30RecipeComponent? Current { get; set; }

    public E30RecipeComponent()
    {
        Current = this;
    }

    #region SC

    [SCEditor("False", "Recipe", "ON-LINE REMOTE 时锁住本地改流程配方、工艺配方：True = 两个配方页新建、改名、保存、删除都拒，只能 Host 改；False = 本地照样能改，改了报给 Host")]
    public bool LockLocalEditInRemote { get; set; }

    #endregion

    #region 接设备

    /// <summary>接到链路和两个配方库上（EAP 组件在链路打开之前调）：挂两个库的上报口，登记 S7 报文。库没装的那个不接。</summary>
    public void Attach(HsmsComponent link, E30Component gem, ISequenceComponent? sequences, IProcessRecipeComponent? processRecipes)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(gem);
        _gem = gem;
        _sequences = sequences;
        _processRecipes = processRecipes;
        if (sequences is not null)
        {
            sequences.E30Callback = this;
        }

        if (processRecipes is not null)
        {
            processRecipes.E30Callback = this;
        }

        link.Handle(7, 1, LoadInquire);
        link.Handle(7, 3, SendProgram);
        link.Handle(7, 5, RequestProgram);
        link.Handle(7, 17, DeletePrograms);
        link.Handle(7, 19, ListPrograms);
    }

    /// <summary>从两个配方库上摘下来（宿主退出时）。</summary>
    public void Detach()
    {
        var sequences = _sequences;
        if (sequences is not null && ReferenceEquals(sequences.E30Callback, this))
        {
            sequences.E30Callback = null;
        }

        var processRecipes = _processRecipes;
        if (processRecipes is not null && ReferenceEquals(processRecipes.E30Callback, this))
        {
            processRecipes.E30Callback = null;
        }
    }

    /// <summary>本地改流程配方、工艺配方现在锁着没有：SC LockLocalEditInRemote 开了、EAP 接着、控制状态是 ON-LINE REMOTE。配方服务改之前问它。</summary>
    public bool IsLocalEditLocked => LockLocalEditInRemote && IsRemote;

    #endregion

    #region 上报（IE30Callback，在 EAP 的上报派发线程上）

    void IE30Callback.SequenceChanged(string name, ChangeKind change)
    {
        ReportChange(name, change);
    }

    void IE30Callback.ProcessRecipeChanged(string name, ChangeKind change)
    {
        ReportChange(name, change);
    }

    /// <summary>报"配方变了"：E30 只有这一个事件，流程配方、工艺配方变了都用它，带名字和怎么变的。</summary>
    private void ReportChange(string name, ChangeKind change)
    {
        _gem?.Report(this, ProcessProgramChange,
            new GemData(DvChangeName, GemValue.Ascii(name)),
            new GemData(DvChangeStatus, (byte)change));
    }

    #endregion

    #region S7 报文

    /// <summary>
    /// S7F1 问能不能下：L[2]{PPID, LENGTH} → S7F2 PPGNT（0 可以、5 不在 ON-LINE REMOTE 不收）。
    /// 下来的覆盖同名的，所以库里已经有也答 0；编号满了、内容不对在 S7F3 存的时候才知道。
    /// </summary>
    private SecsReply LoadInquire(HsmsMessage message)
    {
        SecsRead.List(SecsRead.Body(message), "S7F1", 2);
        return SecsReply.Of(SecsItem.B(IsRemote ? GrantOk : GrantWillNotAccept));
    }

    /// <summary>
    /// S7F3 下：L[2]{PPID, PPBODY} → S7F4 ACKC7（0 收下、1 不让：不在 ON-LINE REMOTE、看不出是哪种、库没过检查）。
    /// 流程配方库里有这个名字就覆盖流程配方、工艺配方库里有就覆盖工艺配方；都没有的看 JSON 的样子，哪个库认就新建在哪个库。
    /// 没收下的原因记日志（ACKC7 带不了原因）。
    /// </summary>
    private SecsReply SendProgram(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S7F3", 2);
        string name = SecsRead.Text(body[0], "PPID").Trim();
        string json = BodyText(body[1]);
        if (!IsRemote)
        {
            LogHelper.Warn(Name, $"Host 下 {name}：不在 ON-LINE REMOTE，不收");
            return Ack(PermissionNotGranted);
        }

        var sequences = _sequences;
        var processRecipes = _processRecipes;
        HandleResult result;
        string what;
        if (sequences is not null && (HasSequence(name) || (!HasProcessRecipe(name) && sequences.AcceptsSequence(json))))
        {
            what = "流程配方";
            result = sequences.ImportSequence(name, json, HostOperator);
        }
        else if (processRecipes is not null && (HasProcessRecipe(name) || processRecipes.AcceptsProcessRecipe(json)))
        {
            what = "工艺配方";
            result = processRecipes.ImportProcessRecipe(name, json, HostOperator);
        }
        else
        {
            LogHelper.Warn(Name, $"Host 下 {name}：两个库里都没有这个名字，内容也看不出是流程配方还是工艺配方，不收");
            return Ack(PermissionNotGranted);
        }

        if (!result.IsSuccess)
        {
            LogHelper.Warn(Name, $"Host 下{what} {name} 没收下：{result.ErrorMessage} [{string.Join(", ", result.Args)}]");
            return Ack(PermissionNotGranted);
        }

        LogHelper.Info(Name, $"Host 下{what} {name}，已存");
        return Ack(Accepted);
    }

    /// <summary>S7F5 要：PPID → S7F6 L[2]{PPID, PPBODY（B，UTF-8 的 JSON）}；两个库里都没有回空表。</summary>
    private SecsReply RequestProgram(HsmsMessage message)
    {
        string name = SecsRead.Text(SecsRead.Body(message), "PPID").Trim();
        string? json = HasSequence(name) ? _sequences?.ExportSequence(name) : _processRecipes?.ExportProcessRecipe(name);
        if (json is null)
        {
            return SecsReply.Of(SecsItem.L());
        }

        return SecsReply.Of(SecsItem.L(SecsItem.A(GemValue.Ascii(name)), SecsItem.B(Encoding.UTF8.GetBytes(json))));
    }

    /// <summary>
    /// S7F17 删：L{PPID}（空表 = 两个库全删）→ S7F18 ACKC7（0 删了、1 不让：不在 ON-LINE REMOTE 或有删不掉的、4 有没有的）。
    /// 先全查一遍，有一个两个库里都没有就一个都不删。
    /// </summary>
    private SecsReply DeletePrograms(HsmsMessage message)
    {
        var names = SecsRead.List(SecsRead.Body(message), "PPID 表").Select(item => SecsRead.Text(item, "PPID").Trim()).ToList();
        if (!IsRemote)
        {
            LogHelper.Warn(Name, "Host 删：不在 ON-LINE REMOTE，不删");
            return Ack(PermissionNotGranted);
        }

        if (names.Count == 0)
        {
            names = SequenceNames().Concat(ProcessRecipeNames()).ToList();
        }

        foreach (string name in names)
        {
            if (!HasSequence(name) && !HasProcessRecipe(name))
            {
                LogHelper.Warn(Name, $"Host 删：两个库里都没有 {name}，一个都不删");
                return Ack(IdNotFound);
            }
        }

        byte ack = Accepted;
        foreach (string name in names)
        {
            bool isSequence = HasSequence(name);
            var result = isSequence ? _sequences!.DeleteSequence(name, HostOperator) : _processRecipes!.DeleteProcessRecipe(name, HostOperator);
            if (!result.IsSuccess)
            {
                LogHelper.Warn(Name, $"Host 删{(isSequence ? "流程配方" : "工艺配方")} {name} 没删掉：{result.ErrorMessage} [{string.Join(", ", result.Args)}]");
                ack = PermissionNotGranted;
            }
        }

        return Ack(ack);
    }

    /// <summary>S7F19 列 → S7F20 L{PPID}：流程配方的名字在前、工艺配方的在后，各自按编号。</summary>
    private SecsReply ListPrograms(HsmsMessage message)
    {
        return SecsReply.Of(SecsItem.L(SequenceNames().Concat(ProcessRecipeNames()).Select(name => SecsItem.A(GemValue.Ascii(name)))));
    }

    #endregion

    private bool IsRemote
    {
        get
        {
            var gem = _gem;
            return gem is not null && gem.IsRemote;
        }
    }

    private IReadOnlyList<string> SequenceNames()
    {
        return _sequences?.SequenceNames ?? [];
    }

    private IReadOnlyList<string> ProcessRecipeNames()
    {
        return _processRecipes?.ProcessRecipeNames ?? [];
    }

    /// <summary>流程配方库里有没有这个名字（不分大小写）。</summary>
    private bool HasSequence(string name)
    {
        return SequenceNames().Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>工艺配方库里有没有这个名字（不分大小写）。</summary>
    private bool HasProcessRecipe(string name)
    {
        return ProcessRecipeNames().Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>PPBODY 转文字：A 原样、B 按 UTF-8；别的格式抛（链路回 S9F7）。</summary>
    private static string BodyText(SecsItem item)
    {
        if (item.Format == SecsFormat.Binary)
        {
            return Encoding.UTF8.GetString(item.GetBinary());
        }

        return SecsRead.Text(item, "PPBODY");
    }

    private static SecsReply Ack(byte code)
    {
        return SecsReply.Of(SecsItem.B(code));
    }
}
