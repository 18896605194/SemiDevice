using xyz.Common.Log;
using xyz.Elements.Attributes;
using xyz.Elements.Enums;
using xyz.Elements.Interfaces;
using xyz.Elements.Models;
using xyz.Secs.Gem;
using xyz.Secs.Gem.Recipe;
using xyz.Shared.Dtos;

namespace xyz.Elements.Elements;

/// <summary>
/// 配方管理（SEMI E30 工艺程序管理，machine.xml 的 Eap 下的 Recipe 节点）：S7 的协议在 xyz.Secs 的 E30RecipeEngine，
/// 这里把流程配方库（ISequenceLibrary）、工艺配方库（IRecipeLibrary）合成它要的一个配方库（IGemRecipes），两个库报上来的"变了"交给它报事件。
/// 流程配方、工艺配方各在自己的库里、不混在一起：Host 那边的配方号（PPID）就是名字，先看流程配方库有没有，再看工艺配方库
/// （两个库的名字不会重）；Host 下一个两个库都还没有的名字，看 JSON 的样子由库自己认（流程配方每一步是分组 + 站点，工艺配方每一步是字段名 → 值）。
/// machine.xml LockLocalEditInRemote：ON-LINE REMOTE 时锁住本地两个配方页（配方服务改之前问 <see cref="IsLocalEditLocked"/>）
/// </summary>
[Element(description: "配方管理（SEMI E30 工艺程序管理）：S7 下、取、删、列流程配方和工艺配方，变了报事件")]
public class E30RecipeElement : ElementBase, IE30Callback, IE30RecipeElement, IGemRecipes, IGemEvents
{
    /// <summary>
    /// Host 改的时候记的操作人
    /// </summary>
    private const string HostOperator = "Host";

    #region DV、事件

    [DataVariable(ValueFormat.String, "变了的流程配方或工艺配方（名字）")]
    public readonly string ChangeNameData = RecipeCodes.ChangeName;

    [DataVariable(ValueFormat.Int, "怎么变的：1 新建、2 修改、3 删除")]
    public readonly string ChangeStatusData = RecipeCodes.ChangeStatus;

    [EventAttribut("流程配方或工艺配方变了（新建、修改、删除；本地配方页和 Host 改的都报）", Data = new[] { RecipeCodes.ChangeName, RecipeCodes.ChangeStatus })]
    public readonly string ProcessProgramChange = RecipeCodes.ProcessProgramChange;

    #endregion

    private IE30Element? _gem;
    private ISequenceLibrary? _sequences;
    private IRecipeLibrary? _processRecipes;
    private E30RecipeEngine? _engine;

    #region Machine

    [Machine("False", "Recipe", "ON-LINE REMOTE 时锁住本地改流程配方、工艺配方：True = 两个配方页新建、改名、保存、删除都拒，只能 Host 改；False = 本地照样能改，改了报给 Host")]
    public bool LockLocalEditInRemote { get; set; }

    #endregion

    #region 接设备

    /// <summary>
    /// 接到链路和两个配方库上（EAP 元件在链路打开之前调）：挂两个库的上报口，登记 S7 报文。库没装的那个不接
    /// </summary>
    public void Attach(IHsms link, IE30Element gem, ISequenceLibrary? sequences, IRecipeLibrary? processRecipes)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(gem);
        var engine = gem.Engine;
        if (engine is null)
        {
            throw new InvalidOperationException($"{Name}：E30 还没接链路");
        }

        _gem = gem;
        _sequences = sequences;
        _processRecipes = processRecipes;
        _engine = new E30RecipeEngine(link.Link, engine, this, this, new SecsLog(this), Name);
        if (sequences is not null)
        {
            sequences.E30Callback = this;
        }

        if (processRecipes is not null)
        {
            processRecipes.E30Callback = this;
        }
    }

    /// <summary>
    /// 从两个配方库上摘下来（宿主退出时）
    /// </summary>
    public void Detach()
    {
        if (_sequences is not null && ReferenceEquals(_sequences.E30Callback, this))
        {
            _sequences.E30Callback = null;
        }

        if (_processRecipes is not null && ReferenceEquals(_processRecipes.E30Callback, this))
        {
            _processRecipes.E30Callback = null;
        }

        _engine = null;
    }

    /// <summary>
    /// 本地改流程配方、工艺配方现在锁着没有：machine.xml LockLocalEditInRemote 开了、EAP 接着、控制状态是 ON-LINE REMOTE。配方服务改之前问它
    /// </summary>
    public bool IsLocalEditLocked => LockLocalEditInRemote && _gem is not null && _gem.IsRemote;

    #endregion

    #region 上报（IE30Callback，在 EAP 的上报派发线程上）

    void IE30Callback.SequenceChanged(string name, ChangeKind change)
    {
        _engine?.ProgramChanged(name, (byte)change);
    }

    void IE30Callback.ProcessRecipeChanged(string name, ChangeKind change)
    {
        _engine?.ProgramChanged(name, (byte)change);
    }

    void IGemEvents.Report(string code, params (string Name, object? Value)[] data)
    {
        RaiseEvent(code, data.Select(item => new GemData(item.Name, item.Value)).ToArray());
    }

    #endregion

    #region 两个配方库合成一个（IGemRecipes）

    /// <summary>
    /// 全部配方名：流程配方的在前、工艺配方的在后，各自按编号
    /// </summary>
    IReadOnlyList<string> IGemRecipes.Names => SequenceNames().Concat(ProcessRecipeNames()).ToList();

    /// <summary>
    /// 流程配方库里有就取流程配方，没有再看工艺配方库
    /// </summary>
    string? IGemRecipes.Body(string name)
    {
        if (HasSequence(name))
        {
            return _sequences?.GetSequenceData(name);
        }

        return _processRecipes?.GetRecipe(name);
    }

    /// <summary>
    /// 流程配方库里有这个名字就覆盖流程配方、工艺配方库里有就覆盖工艺配方；都没有的看 JSON 的样子，哪个库认就新建在哪个库
    /// </summary>
    string? IGemRecipes.Store(string name, string body)
    {
        HandleResult result;
        string what;
        if (_sequences is not null && (HasSequence(name) || (!HasProcessRecipe(name) && _sequences.AcceptsSequence(body))))
        {
            what = "流程配方";
            result = _sequences.ImportSequence(name, body, HostOperator);
        }
        else if (_processRecipes is not null && (HasProcessRecipe(name) || _processRecipes.AcceptsRecipe(body)))
        {
            what = "工艺配方";
            result = _processRecipes.ImportRecipe(name, body, HostOperator);
        }
        else
        {
            return "两个库里都没有这个名字，内容也看不出是流程配方还是工艺配方";
        }

        if (!result.IsSuccess)
        {
            return $"{what}没过检查：{result.ErrorMessage} [{string.Join(", ", result.Args)}]";
        }

        LogHelper.Info(this, $"Host 下的 {name} 存成了{what}");
        return null;
    }

    /// <summary>
    /// 流程配方库里有就删流程配方，不然删工艺配方
    /// </summary>
    string? IGemRecipes.Delete(string name)
    {
        HandleResult result;
        string what;
        if (_sequences is not null && HasSequence(name))
        {
            what = "流程配方";
            result = _sequences.DeleteSequence(name, HostOperator);
        }
        else if (_processRecipes is not null)
        {
            what = "工艺配方";
            result = _processRecipes.DeleteRecipe(name, HostOperator);
        }
        else
        {
            return "没装配方库";
        }

        if (!result.IsSuccess)
        {
            return $"{what}删不掉：{result.ErrorMessage} [{string.Join(", ", result.Args)}]";
        }

        return null;
    }

    private IReadOnlyList<string> SequenceNames()
    {
        return _sequences?.SequenceNames ?? [];
    }

    private IReadOnlyList<string> ProcessRecipeNames()
    {
        return _processRecipes?.RecipeNames ?? [];
    }

    /// <summary>
    /// 流程配方库里有没有这个名字（不分大小写）
    /// </summary>
    private bool HasSequence(string name)
    {
        return SequenceNames().Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 工艺配方库里有没有这个名字（不分大小写）
    /// </summary>
    private bool HasProcessRecipe(string name)
    {
        return ProcessRecipeNames().Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    #endregion
}
