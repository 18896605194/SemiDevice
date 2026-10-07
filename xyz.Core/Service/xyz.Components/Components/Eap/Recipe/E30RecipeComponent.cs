using System.Text;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Components.Models;
using xyz.Configs.Models;
using xyz.Secs.Hsms;
using xyz.Secs.SecsII;

namespace xyz.Components.Components;

/// <summary>
/// 配方管理（SEMI E30 工艺程序管理，sc.xml 的 Eap 下的 Recipe 节点）：Host 的 S7 翻成配方库的命令（IRecipeLibrary，跟本地配方页同一套检查），
/// 配方库报上来的"配方变了"翻成事件。配方只在设备侧的流程配方库、工艺配方库里有一份。
/// Host 那边的配方号（PPID）= 前缀 + 配方名：流程配方 SequencePrefix（默认 SEQ/）、工艺配方 ProcessRecipePrefix（默认 PR/）；
/// 配方名只有字母、数字、_ 和 -，前缀带 / 就不会跟名字撞。配方内容（PPBODY）是 JSON，发出去用 B（UTF-8），收的时候 A、B 都认。
/// ① S7F1 问能不能下 → S7F2 PPGNT；② S7F3 下配方（没有就新建、有就覆盖）→ S7F4 ACKC7；③ S7F5 要配方 → S7F6；
/// ④ S7F17 删配方（空表 = 全删）→ S7F18 ACKC7；⑤ S7F19 列配方 → S7F20。下、删要 ON-LINE REMOTE；要、列在 ON-LINE 就行（离线由 E30 挡掉）。
/// SC LockLocalEditInRemote：ON-LINE REMOTE 时锁住本地配方页（配方服务改之前问 <see cref="IsLocalEditLocked"/>）。
/// </summary>
[Component(description: "配方管理（SEMI E30 工艺程序管理）：S7 下、取、删、列配方，配方变了报事件")]
public class E30RecipeComponent : ComponentBase, IE30RecipeCallback
{
    /// <summary>PPGNT：可以下。</summary>
    private const byte GrantOk = 0;

    /// <summary>PPGNT：配方号不对（不是两个库的前缀、库没装、名字空）。</summary>
    private const byte GrantInvalidId = 3;

    /// <summary>PPGNT：不收（不在 ON-LINE REMOTE）。</summary>
    private const byte GrantWillNotAccept = 5;

    /// <summary>ACKC7：收下了。</summary>
    private const byte Accepted = 0;

    /// <summary>ACKC7：不让做（不在 ON-LINE REMOTE，或配方库没过检查、删不掉）。</summary>
    private const byte PermissionNotGranted = 1;

    /// <summary>ACKC7：配方号不对或没有这个配方。</summary>
    private const byte IdNotFound = 4;

    /// <summary>Host 改配方时记的操作人。</summary>
    private const string HostOperator = "Host";

    #region DV、事件

    private const string DvChangeName = "PPChangeName";
    private const string DvChangeStatus = "PPChangeStatus";

    [DataVariable(ValueFormat.String, "变了的配方（Host 那边的配方号：前缀 + 配方名）")]
    public readonly string ChangeNameData = DvChangeName;

    [DataVariable(ValueFormat.Int, "配方怎么变的：1 新建、2 修改、3 删除")]
    public readonly string ChangeStatusData = DvChangeStatus;

    [EventAttribut("配方变了（新建、修改、删除；本地配方页和 Host 改的都报）", Data = new[] { DvChangeName, DvChangeStatus })]
    public readonly string ProcessProgramChange = "ProcessProgramChange";

    #endregion

    private E30Component? _gem;
    private IRecipeLibrary? _sequences;
    private IRecipeLibrary? _processRecipes;

    /// <summary>当前配方管理组件；Eap 下没配 Recipe 节点时为 null（本地配方页就不会被锁）。</summary>
    public static E30RecipeComponent? Current { get; set; }

    public E30RecipeComponent()
    {
        Current = this;
    }

    #region SC

    [SCEditor("SEQ/", "Recipe", "流程配方在 Host 那边的配方号前缀（配方号 = 前缀 + 配方名）；Host 建 PJ 给的配方号带不带这个前缀都认")]
    public string SequencePrefix { get; set; } = "SEQ/";

    [SCEditor("PR/", "Recipe", "工艺配方在 Host 那边的配方号前缀；可以有一个空着（空的那个库收不带前缀的配方号），两个不能一样")]
    public string ProcessRecipePrefix { get; set; } = "PR/";

    [SCEditor("False", "Recipe", "ON-LINE REMOTE 时锁住本地改配方：True = 配方页新建、改名、保存、删除都拒，只能 Host 改；False = 本地照样能改，改了报给 Host")]
    public bool LockLocalEditInRemote { get; set; }

    #endregion

    /// <summary>装配读完 SC：两个前缀一样就分不出配方号是哪个库的，开机就报出来。</summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);
        if (string.Equals(SequencePrefix, ProcessRecipePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"sc.xml 节点 {Name} 的 SequencePrefix 和 ProcessRecipePrefix 不能一样（现在都是\"{SequencePrefix}\"）：分不出配方号是哪个库的");
        }
    }

    #region 接设备

    /// <summary>接到链路和两个配方库上（EAP 组件在链路打开之前调）：挂配方库的上报口，登记 S7 报文。库没装的那个不接。</summary>
    public void Attach(HsmsComponent link, E30Component gem, IRecipeLibrary? sequences, IRecipeLibrary? processRecipes)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(gem);
        _gem = gem;
        _sequences = sequences;
        _processRecipes = processRecipes;
        if (sequences is not null)
        {
            sequences.E30RecipeCallback = this;
        }

        if (processRecipes is not null)
        {
            processRecipes.E30RecipeCallback = this;
        }

        link.Handle(7, 1, LoadInquire);
        link.Handle(7, 3, SendRecipe);
        link.Handle(7, 5, RequestRecipe);
        link.Handle(7, 17, DeleteRecipes);
        link.Handle(7, 19, ListRecipes);
    }

    /// <summary>从配方库上摘下来（宿主退出时）。</summary>
    public void Detach()
    {
        foreach (var library in new[] { _sequences, _processRecipes })
        {
            if (library is not null && ReferenceEquals(library.E30RecipeCallback, this))
            {
                library.E30RecipeCallback = null;
            }
        }
    }

    #endregion

    #region 给本地配方页、E40 用的

    /// <summary>本地改配方现在锁着没有：SC LockLocalEditInRemote 开了、EAP 接着、控制状态是 ON-LINE REMOTE。配方服务改之前问它。</summary>
    public bool IsLocalEditLocked
    {
        get
        {
            var gem = _gem;
            return LockLocalEditInRemote && gem is not null && gem.IsRemote;
        }
    }

    /// <summary>Host 给的配方号（建 PJ 的 RCPSPEC）对应的流程配方名：带流程配方前缀的去掉前缀，不带的原样（Host 直接给名字也认）。</summary>
    public string SequenceNameOf(string recipeId)
    {
        string prefix = SequencePrefix;
        return prefix.Length > 0 && recipeId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? recipeId[prefix.Length..] : recipeId;
    }

    /// <summary>流程配方名在 Host 那边的配方号（前缀 + 名字），报 PJ 的配方用。</summary>
    public string SequenceIdOf(string name)
    {
        return SequencePrefix + name;
    }

    #endregion

    #region 上报（IE30RecipeCallback，在 EAP 的上报派发线程上）

    void IE30RecipeCallback.RecipeChanged(IRecipeLibrary library, string name, RecipeChange change)
    {
        _gem?.Report(this, ProcessProgramChange,
            new GemData(DvChangeName, GemValue.Ascii(IdOf(library, name))),
            new GemData(DvChangeStatus, (byte)change));
    }

    #endregion

    #region S7 报文

    /// <summary>
    /// S7F1 问能不能下：L[2]{PPID, LENGTH} → S7F2 PPGNT（0 可以、3 配方号不对、5 不在 ON-LINE REMOTE 不收）。
    /// 下来的配方覆盖同名的，所以库里已经有也答 0；编号满了、内容不对在 S7F3 存的时候才知道。
    /// </summary>
    private SecsReply LoadInquire(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S7F1", 2);
        string id = SecsRead.Text(body[0], "PPID").Trim();
        byte grant = !IsRemote ? GrantWillNotAccept : Resolve(id) is null ? GrantInvalidId : GrantOk;
        return SecsReply.Of(SecsItem.B(grant));
    }

    /// <summary>
    /// S7F3 下配方：L[2]{PPID, PPBODY} → S7F4 ACKC7（0 收下、1 不让：不在 ON-LINE REMOTE 或配方库没过检查、4 配方号不对）。
    /// 库里没有就新建、有就覆盖；没收下的原因记日志（ACKC7 带不了原因）。
    /// </summary>
    private SecsReply SendRecipe(HsmsMessage message)
    {
        var body = SecsRead.List(SecsRead.Body(message), "S7F3", 2);
        string id = SecsRead.Text(body[0], "PPID").Trim();
        string json = BodyText(body[1]);
        if (!IsRemote)
        {
            LogHelper.Warn(Name, $"Host 下配方 {id}：不在 ON-LINE REMOTE，不收");
            return Ack(PermissionNotGranted);
        }

        var target = Resolve(id);
        if (target is null)
        {
            LogHelper.Warn(Name, $"Host 下配方 {id}：认不出是哪个库的（前缀 {SequencePrefix} / {ProcessRecipePrefix}），不收");
            return Ack(IdNotFound);
        }

        var (library, name) = target.Value;
        var result = library.ImportRecipe(name, json, HostOperator);
        if (!result.IsSuccess)
        {
            LogHelper.Warn(Name, $"Host 下配方 {id} 没收下：{result.ErrorMessage} [{string.Join(", ", result.Args)}]");
            return Ack(PermissionNotGranted);
        }

        LogHelper.Info(Name, $"Host 下配方 {id}，已存");
        return Ack(Accepted);
    }

    /// <summary>S7F5 要配方：PPID → S7F6 L[2]{PPID, PPBODY（B，UTF-8 的 JSON）}；配方号不对、没有这个配方回空表。</summary>
    private SecsReply RequestRecipe(HsmsMessage message)
    {
        string id = SecsRead.Text(SecsRead.Body(message), "PPID").Trim();
        var target = Resolve(id);
        string? json = target is null ? null : target.Value.Library.ExportRecipe(target.Value.Name);
        if (json is null)
        {
            return SecsReply.Of(SecsItem.L());
        }

        return SecsReply.Of(SecsItem.L(SecsItem.A(GemValue.Ascii(id)), SecsItem.B(Encoding.UTF8.GetBytes(json))));
    }

    /// <summary>
    /// S7F17 删配方：L{PPID}（空表 = 两个库全删）→ S7F18 ACKC7（0 删了、1 不让：不在 ON-LINE REMOTE 或有删不掉的、4 有配方号不对或没有）。
    /// 先全查一遍，有一个配方号不对或没有就一个都不删。
    /// </summary>
    private SecsReply DeleteRecipes(HsmsMessage message)
    {
        var ids = SecsRead.List(SecsRead.Body(message), "PPID 表").Select(item => SecsRead.Text(item, "PPID").Trim()).ToList();
        if (!IsRemote)
        {
            LogHelper.Warn(Name, "Host 删配方：不在 ON-LINE REMOTE，不删");
            return Ack(PermissionNotGranted);
        }

        if (ids.Count == 0)
        {
            ids = AllIds().ToList();
        }

        var targets = new List<(IRecipeLibrary Library, string Name, string Id)>();
        foreach (string id in ids)
        {
            var target = Resolve(id);
            if (target is null || !target.Value.Library.RecipeNames.Contains(target.Value.Name, StringComparer.OrdinalIgnoreCase))
            {
                LogHelper.Warn(Name, $"Host 删配方：没有 {id}，一个都不删");
                return Ack(IdNotFound);
            }

            targets.Add((target.Value.Library, target.Value.Name, id));
        }

        byte ack = Accepted;
        foreach (var (library, name, id) in targets)
        {
            var result = library.DeleteRecipe(name, HostOperator);
            if (!result.IsSuccess)
            {
                LogHelper.Warn(Name, $"Host 删配方 {id} 没删掉：{result.ErrorMessage} [{string.Join(", ", result.Args)}]");
                ack = PermissionNotGranted;
            }
        }

        return Ack(ack);
    }

    /// <summary>S7F19 列配方 → S7F20 L{PPID}：两个库的配方号（流程配方在前），各自按编号。</summary>
    private SecsReply ListRecipes(HsmsMessage message)
    {
        return SecsReply.Of(SecsItem.L(AllIds().Select(id => SecsItem.A(GemValue.Ascii(id)))));
    }

    #endregion

    #region 配方号

    private bool IsRemote
    {
        get
        {
            var gem = _gem;
            return gem is not null && gem.IsRemote;
        }
    }

    /// <summary>
    /// 配方号 → 哪个库、配方名：前缀长的先认（空前缀最后，收剩下的）；认不出、那个库没装、去掉前缀名字是空的返回 null。
    /// </summary>
    private (IRecipeLibrary Library, string Name)? Resolve(string recipeId)
    {
        var candidates = new[] { (Prefix: SequencePrefix, Library: _sequences), (Prefix: ProcessRecipePrefix, Library: _processRecipes) }
            .OrderByDescending(candidate => candidate.Prefix.Length);
        foreach (var (prefix, library) in candidates)
        {
            if (!recipeId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = recipeId[prefix.Length..];
            if (library is null || name.Length == 0)
            {
                return null;
            }

            return (library, name);
        }

        return null;
    }

    /// <summary>某个库里的配方名在 Host 那边的配方号。</summary>
    private string IdOf(IRecipeLibrary library, string name)
    {
        return (ReferenceEquals(library, _sequences) ? SequencePrefix : ProcessRecipePrefix) + name;
    }

    /// <summary>两个库全部的配方号：流程配方在前，各自按编号。</summary>
    private IEnumerable<string> AllIds()
    {
        foreach (var library in new[] { _sequences, _processRecipes })
        {
            if (library is null)
            {
                continue;
            }

            foreach (string name in library.RecipeNames)
            {
                yield return IdOf(library, name);
            }
        }
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

    #endregion
}
