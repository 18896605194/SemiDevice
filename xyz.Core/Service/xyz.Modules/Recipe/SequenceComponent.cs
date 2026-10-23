using System.Globalization;
using System.Text.Json;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Components.Enums;
using xyz.Components.Interfaces;
using xyz.Configs.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Modules;

[Component(description: "流程配方库：编号 1~N 的流程配方，一个编号一个文件")]
public class SequenceComponent : ComponentBase, ISequenceComponent
{
    /// <summary>
    /// 001.xml
    /// </summary>
    private const string FileNumberFormat = "D3";

    private const string FileExtension = ".xml";

    /// <summary>
    /// 先写到临时文件再换过去，放置断电
    /// </summary>
    private const string TempExtension = ".tmp";

    private readonly object _gate = new();

    /// <summary>
    /// 支持排序的 字典
    /// </summary>
    private readonly SortedDictionary<int, SequenceData> _items = new();

    /// <summary>
    /// 所有的站点分组
    /// </summary>
    private IReadOnlyList<SequenceStationGroup> _groups = [];

    private IRecipeComponent? _recipes;
    private IEapNotifierComponent? _notifier;

    /// <summary>
    /// 整机晶圆尺寸：切了尺寸机械手换站点表，分组要跟着重新生成
    /// </summary>
    private IWaferSize? _waferSize;

    /// <summary>
    /// 开机 Bind 时给的模块：切尺寸重新生成分组用
    /// </summary>
    private IReadOnlyList<IModule> _modules = [];

    #region SC

    [SCEditor("99", "Sequence", "流程配方个数：编号 1~N")]
    public int Capacity { get; set; } = 99;

    [SCEditor("Recipe\\Sequence", "Sequence", "流程配方文件目录：相对宿主运行目录，也可以写绝对路径；一个编号一个文件")]
    public string Folder { get; set; } = "Recipe\\Sequence";

    [SCEditor("32", "Sequence", "流程配方名称最多几个字符")]
    public int NameMaxLength { get; set; } = 32;

    #endregion

    #region 装配：查参数、读文件、生成可选站点分组

    /// <summary>
    /// 可选的站点分组
    /// </summary>
    public IReadOnlyList<SequenceStationGroup> StationGroups
    {
        get
        {
            lock (_gate)
            {
                return _groups;
            }
        }
    }

    /// <summary>
    /// 装配加载
    /// </summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);

        if (Capacity < 1)
        {
            throw new InvalidOperationException($"sc.xml 节点 {Name} 的 Capacity 要大于 0，现在是 {Capacity}");
        }

        if (NameMaxLength < 1)
        {
            throw new InvalidOperationException($"sc.xml 节点 {Name} 的 NameMaxLength 要大于 0，现在是 {NameMaxLength}");
        }

        if (string.IsNullOrWhiteSpace(Folder))
        {
            throw new InvalidOperationException($"sc.xml 节点 {Name} 没配 Folder（流程配方文件放哪）");
        }

        Load();
    }

    public void Load()
    {
        string folder = FolderPath();
        var loaded = new SortedDictionary<int, SequenceData>();
        try
        {
            Directory.CreateDirectory(folder);
            foreach (string file in Directory.EnumerateFiles(folder, "*" + FileExtension))
            {
                var data = ReadFile(file);
                if (data is null)
                {
                    continue;
                }

                if (!loaded.TryAdd(data.Index, data))
                {
                    LogHelper.Warn(Name, $"跳过 {file}：{data.Index} 号已经有一个文件了（文件名写法不同但编号一样）");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogHelper.Error(Name, $"流程配方目录打不开：{folder}（{exception.Message}），列表先是空的");
        }

        lock (_gate)
        {
            _items.Clear();
            foreach (var pair in loaded)
            {
                _items[pair.Key] = pair.Value;
            }
        }

        LogHelper.Info(Name, $"读入流程配方 {loaded.Count} 个（目录 {folder}）");
    }

    /// <summary>
    /// 开机：按装起来的模块生成可选站点分组
    /// </summary>
    public override void Bind()
    {
        Bind(IocHelper.GetServices<IModule>());
    }

    /// <summary>
    /// 生成可选站点分组（见 <see cref="BuildGroups"/>）；工艺配方库、EAP 派发、晶圆尺寸也在这里取一次
    /// </summary>
    public void Bind(IEnumerable<IModule> modules)
    {
        _recipes = IocHelper.GetService<IRecipeComponent>();
        _notifier = IocHelper.GetService<IEapNotifierComponent>();

        // 先摘后挂：Bind 能重入，保证只挂一份
        if (_waferSize is not null)
        {
            _waferSize.Changed -= OnWaferSizeChanged;
        }

        _waferSize = IocHelper.GetService<IWaferSize>();
        if (_waferSize is not null)
        {
            _waferSize.Changed += OnWaferSizeChanged;
        }

        _modules = modules.ToList();
        BuildGroups();
    }

    /// <summary>
    /// 切了晶圆尺寸：按新尺寸的站点表重新生成分组，发一条编号 0 的变更让流程配方页重拉
    /// </summary>
    private void OnWaferSizeChanged(int size)
    {
        BuildGroups();
        EventBus.Send(new SequenceChangedDto(), SequenceListDto.EventToken, retain: false);
    }

    /// <summary>
    /// 生成可选站点分组：按 sc.xml 的分组节点（模块全路径的第一层，如 Chamber.Chamber1 的 Chamber），
    /// 只留能放片、机械手到得了的站点（机械手现在这种尺寸的站点表里配了的）；顶层直接装的站点自己成一组
    /// </summary>
    private void BuildGroups()
    {
        var all = _modules;
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var robot in all.OfType<ITransferRobot>())
        {
            reachable.UnionWith(robot.Stations.Keys);
        }

        var groups = new List<SequenceStationGroup>();
        foreach (var group in all.Where(module => module is ITransferStation && reachable.Contains(module.Name)).GroupBy(GroupOf))
        {
            var stations = group.ToList();
            groups.Add(new SequenceStationGroup(
                group.Key,
                stations.Select(station => station.Name).ToList(),
                stations.All(station => station is ILoadPort),
                stations.Any(station => station is IProcessStation)));
        }

        lock (_gate)
        {
            _groups = groups;
        }

        if (groups.Count == 0)
        {
            LogHelper.Info(Name, "没有可选的站点分组（没有机械手，或机械手站点表里的站点都没装），流程配方编辑不了步骤");
            return;
        }

        LogHelper.Info(Name, $"可选站点分组 {groups.Count} 个：{string.Join("；", groups.Select(group => $"{group.Name}（{string.Join(", ", group.Modules)}）"))}");
    }

    /// <summary>
    /// 站点在 sc.xml 里的分组：全路径的第一层；顶层直接装的就是它自己
    /// </summary>
    private static string GroupOf(IModule module)
    {
        int dot = module.FullPath.IndexOf('.');
        if (dot < 0)
        {
            return module.Name;
        }

        return module.FullPath[..dot];
    }

    #endregion

    #region 查询

    public IReadOnlyList<SequenceData> List()
    {
        lock (_gate)
        {
            return _items.Values.Select(item => item.Clone()).ToList();
        }
    }

    public SequenceData? Find(string name)
    {
        lock (_gate)
        {
            return FindByName(name.Trim())?.Clone();
        }
    }

    public HandleResult Get(int index)
    {
        lock (_gate)
        {
            return CheckExists(index) ?? HandleResult.Success(_items[index].Clone());
        }
    }

    #endregion

    #region 新建、改名、删除、保存

    public HandleResult Create(int index, string name, string operatorName)
    {
        name = name.Trim();
        HandleResult result;
        lock (_gate)
        {
            var now = DateTime.Now;
            result = CheckIndex(index) ?? CheckFree(index) ?? CheckName(name, index) ?? Store(new SequenceData
            {
                Index = index,
                Name = name,
                CreatedBy = operatorName,
                CreatedAt = now,
                ModifiedBy = operatorName,
                ModifiedAt = now,
                Revision = 1,
                Steps = DefaultSteps(),
            });
        }

        Report(result, index, $"新建流程配方 {index} 号 {name}（操作人 {operatorName}）", (name, ChangeKind.Created));
        return result;
    }

    public HandleResult Rename(int index, string name, string operatorName)
    {
        name = name.Trim();
        HandleResult result;
        string oldName = string.Empty;
        lock (_gate)
        {
            var rejected = CheckExists(index) ?? CheckName(name, index);
            if (rejected is null)
            {
                oldName = _items[index].Name;
            }

            result = rejected ?? Store(Touch(_items[index], operatorName, next => next.Name = name));
        }

        // Host 那边按名字认配方：改名 = 旧名删了、新名建了
        Report(result, index, $"流程配方 {index} 号改名为 {name}（操作人 {operatorName}）", (oldName, ChangeKind.Deleted), (name, ChangeKind.Created));
        return result;
    }

    public HandleResult Delete(int index, string operatorName)
    {
        HandleResult result;
        string name = string.Empty;
        lock (_gate)
        {
            result = CheckExists(index) ?? Remove(index, out name);
        }

        Report(result, index, $"删除流程配方 {index} 号 {name}（操作人 {operatorName}）", (name, ChangeKind.Deleted));
        return result;
    }

    public HandleResult Save(int index, int revision, string description, IReadOnlyList<SequenceStep> steps, string operatorName)
    {
        // 工艺配方要在工艺配方库里（没装库就不查）。库只被这边查、从不反过来调这边，两把锁不会互等
        var recipes = _recipes;
        HandleResult result;
        lock (_gate)
        {
            var groups = _groups;
            result = CheckExists(index)
                ?? CheckRevision(index, revision)
                ?? CheckSteps(steps, groups, recipes)
                ?? Store(Touch(_items[index], operatorName, next =>
                {
                    next.Description = description.Trim();
                    next.Steps = Normalize(steps, groups);
                }));
        }

        Report(result, index, $"保存流程配方 {index} 号（操作人 {operatorName}）", ((result.Result as SequenceData)?.Name ?? string.Empty, ChangeKind.Edited));
        return result;
    }

    /// <summary>
    /// 记一条日志、发变更事件
    /// </summary>
    /// <param name="result"></param>
    /// <param name="index"></param>
    /// <param name="message"></param>
    /// <param name="changes"></param>
    private void Report(HandleResult result, int index, string message, params (string Name, ChangeKind Change)[] changes)
    {
        if (!result.IsSuccess)
        {
            return;
        }

        var saved = result.Result as SequenceData;
        LogHelper.Info(Name, saved is null ? message : $"{message}，版本 {saved.Revision}");
        EventBus.Send(new SequenceChangedDto { Index = index }, SequenceListDto.EventToken, retain: false);

        var callback = E30Callback;
        if (callback is null)
        {
            return;
        }

        foreach (var (name, change) in changes)
        {
            _notifier?.Post(() => callback.SequenceChanged(name, change));
        }
    }

    #endregion

    #region EAP 口子

    public IE30Callback? E30Callback { get; set; }

    public IReadOnlyList<string> SequenceNames
    {
        get
        {
            lock (_gate)
            {
                return _items.Values.Select(item => item.Name).ToList();
            }
        }
    }

    public string? GetSequenceData(string name)
    {
        var found = Find(name);
        return found is null ? null : JsonHelper.Serialize(found);
    }

    public bool AcceptsSequence(string json)
    {
        return JsonSteps.AllHave(json, "group");
    }

    public HandleResult ImportSequence(string name, string json, string operatorName)
    {
        name = name.Trim();
        var body = ParseBody(json);
        if (body is null)
        {
            return HandleResult.Fail(ErrorCodes.SequenceBodyInvalid, name);
        }

        // 工艺配方库只被这边查、从不反过来调这边，两把锁不会互等
        var recipes = _recipes;
        HandleResult result;
        int index;
        string reported;
        bool created;
        lock (_gate)
        {
            var groups = _groups;
            var existing = FindByName(name);
            created = existing is null;
            if (existing is null)
            {
                #region 新名字：放在第一个空编号上新建（版本 1）

                index = FirstFreeIndex();
                reported = name;
                var now = DateTime.Now;
                result = (index == 0 ? HandleResult.Fail(ErrorCodes.SequenceFull, Text(Capacity)) : null)
                    ?? CheckName(name, index)
                    ?? CheckSteps(body.Steps, groups, recipes)
                    ?? Store(new SequenceData
                    {
                        Index = index,
                        Name = name,
                        Description = body.Description.Trim(),
                        CreatedBy = operatorName,
                        CreatedAt = now,
                        ModifiedBy = operatorName,
                        ModifiedAt = now,
                        Revision = 1,
                        Steps = Normalize(body.Steps, groups),
                    });

                #endregion
            }
            else
            {
                #region 已有的名字：说明和步骤整个换掉（版本加 1）

                index = existing.Index;
                reported = existing.Name;
                result = CheckSteps(body.Steps, groups, recipes) ?? Store(Touch(existing, operatorName, next =>
                {
                    next.Description = body.Description.Trim();
                    next.Steps = Normalize(body.Steps, groups);
                }));

                #endregion
            }
        }

        Report(result, index, $"{(created ? "远程新建" : "远程覆盖")}流程配方 {index} 号 {reported}（操作人 {operatorName}）",
            (reported, created ? ChangeKind.Created : ChangeKind.Edited));
        return result;
    }

    public HandleResult DeleteSequence(string name, string operatorName)
    {
        name = name.Trim();
        HandleResult result;
        int index;
        string removed;
        lock (_gate)
        {
            var existing = FindByName(name);
            if (existing is null)
            {
                return HandleResult.Fail(ErrorCodes.SequenceNameNotFound, name);
            }

            index = existing.Index;
            result = Remove(index, out removed);
        }

        Report(result, index, $"远程删除流程配方 {index} 号 {removed}（操作人 {operatorName}）", (removed, ChangeKind.Deleted));
        return result;
    }

    /// <summary>
    /// Host 给的 JSON 转成流程配方（只用说明和步骤）；读不出来返回 null。空着的说明、步骤、站点、配方补成空的，后面的检查照常查。
    /// </summary>
    private static SequenceData? ParseBody(string json)
    {
        SequenceData? body;
        try
        {
            body = JsonHelper.Deserialize<SequenceData>(json);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            return null;
        }

        if (body is null)
        {
            return null;
        }

        body.Description = body.Description ?? string.Empty;
        body.Steps = (body.Steps ?? [])
            .Where(step => step is not null)
            .Select(step => new SequenceStep(step.Group ?? string.Empty, (step.Stations ?? []).Where(station => station is not null), step.Recipe ?? string.Empty))
            .ToList();
        return body;
    }

    #endregion

    #region 内部：查找、默认步骤、改版本

    private SequenceData? FindByName(string name)
    {
        return _items.Values.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private int FirstFreeIndex()
    {
        for (int index = 1; index <= Capacity; index++)
        {
            if (!_items.ContainsKey(index))
            {
                return index;
            }
        }

        return 0;
    }

    /// <summary>
    /// 新建 默认步骤
    /// </summary>
    /// <returns></returns>
    private List<SequenceStep> DefaultSteps()
    {
        var port = _groups.FirstOrDefault(group => group.IsLoadPort);
        var process = _groups.FirstOrDefault(group => group.NeedsRecipe) ?? _groups.FirstOrDefault(group => !group.IsLoadPort);
        var steps = new List<SequenceStep>();
        if (port is not null)
        {
            steps.Add(new SequenceStep(port.Name, port.Modules));
        }

        if (process is not null)
        {
            steps.Add(new SequenceStep(process.Name, process.Modules));
        }

        if (port is not null)
        {
            steps.Add(new SequenceStep(port.Name, port.Modules));
        }

        return steps;
    }

    /// <summary>
    /// 改一个流程配方：在副本上改，版本加 1，记修改人、修改时间；写文件成了才换进库里。
    /// </summary>
    private static SequenceData Touch(SequenceData current, string operatorName, Action<SequenceData> change)
    {
        var next = current.Clone();
        change(next);
        next.ModifiedBy = operatorName;
        next.ModifiedAt = DateTime.Now;
        next.Revision = current.Revision + 1;
        return next;
    }

    #endregion

    #region 内部：读写文件

    /// <summary>
    /// 写文件（先写临时文件再换过去），成了才换进内存；写不进去内存里的不动，把原因回给界面。必须在 _gate 里调。
    /// </summary>
    private HandleResult Store(SequenceData data)
    {
        string file = FilePath(data.Index);
        string temp = file + TempExtension;
        try
        {
            Directory.CreateDirectory(FolderPath());
            XmlHelper.Serialize(temp, data);
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LogHelper.Error(Name, $"{data.Index} 号流程配方写文件失败，库里的没改：{exception.Message}");
            DeleteTemp(temp);
            return HandleResult.Fail(ErrorCodes.SequenceSaveFailed, Text(data.Index), exception.Message);
        }

        _items[data.Index] = data;
        return HandleResult.Success(data.Clone());
    }

    /// <summary>
    /// 删文件
    /// </summary>
    private HandleResult Remove(int index, out string name)
    {
        name = _items[index].Name;
        try
        {
            File.Delete(FilePath(index));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogHelper.Error(Name, $"{index} 号流程配方文件删不掉，库里的没动：{exception.Message}");
            return HandleResult.Fail(ErrorCodes.SequenceSaveFailed, Text(index), exception.Message);
        }

        _items.Remove(index);
        return HandleResult.Success();
    }

    /// <summary>
    /// 写失败时留下的临时文件顺手删掉；删不掉也没关系，下次写会覆盖。
    /// </summary>
    private void DeleteTemp(string temp)
    {
        try
        {
            File.Delete(temp);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogHelper.Warn(Name, $"临时文件删不掉（下次写会覆盖）：{temp}（{exception.Message}）");
        }
    }

    /// <summary>
    /// 读一个文件：文件名就是编号；名字不是 1~Capacity 的编号、或内容读不出来的跳过（返回 null）。
    /// </summary>
    private SequenceData? ReadFile(string file)
    {
        if (!int.TryParse(System.IO.Path.GetFileNameWithoutExtension(file), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            || index < 1 || index > Capacity)
        {
            LogHelper.Warn(Name, $"跳过 {file}：文件名不是 1~{Capacity} 的编号");
            return null;
        }

        try
        {
            var data = XmlHelper.Deserialize<SequenceData>(file);
            if (data is null)
            {
                LogHelper.Warn(Name, $"跳过 {file}：内容是空的");
                return null;
            }

            data.Index = index;
            return data;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LogHelper.Error(Name, $"跳过 {file}：读不出来（{exception.Message}）");
            return null;
        }
    }

    private string FolderPath()
    {
        return System.IO.Path.IsPathRooted(Folder) ? Folder : System.IO.Path.Combine(AppContext.BaseDirectory, Folder);
    }

    private string FilePath(int index)
    {
        return System.IO.Path.Combine(FolderPath(), index.ToString(FileNumberFormat, CultureInfo.InvariantCulture) + FileExtension);
    }

    #endregion

    #region 内部：检查、规整

    private HandleResult? CheckIndex(int index)
    {
        return index < 1 || index > Capacity
            ? HandleResult.Fail(ErrorCodes.SequenceIndexOutOfRange, Text(index), Text(Capacity))
            : null;
    }

    private HandleResult? CheckExists(int index)
    {
        return CheckIndex(index) ?? (_items.ContainsKey(index) ? null : HandleResult.Fail(ErrorCodes.SequenceNotFound, Text(index)));
    }

    private HandleResult? CheckFree(int index)
    {
        return _items.TryGetValue(index, out var existing)
            ? HandleResult.Fail(ErrorCodes.SequenceIndexOccupied, Text(index), existing.Name)
            : null;
    }

    private HandleResult? CheckRevision(int index, int revision)
    {
        return _items[index].Revision == revision ? null : HandleResult.Fail(ErrorCodes.SequenceRevisionMismatch, Text(index));
    }

    private HandleResult? CheckName(string name, int selfIndex)
    {
        if (name.Length == 0)
        {
            return HandleResult.Fail(ErrorCodes.SequenceNameRequired);
        }

        if (name.Length > NameMaxLength)
        {
            return HandleResult.Fail(ErrorCodes.SequenceNameTooLong, Text(NameMaxLength));
        }

        foreach (var pair in _items)
        {
            if (pair.Key != selfIndex && string.Equals(pair.Value.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return HandleResult.Fail(ErrorCodes.SequenceNameDuplicate, name, Text(pair.Key));
            }
        }

        return null;
    }

    /// <summary>
    /// 检查sequence的每一步
    /// </summary>
    /// <param name="steps"></param>
    /// <param name="groups"></param>
    /// <param name="recipes"></param>
    /// <returns></returns>
    private static HandleResult? CheckSteps(IReadOnlyList<SequenceStep> steps, IReadOnlyList<SequenceStationGroup> groups, IRecipeComponent? recipes)
    {
        for (int position = 0; position < steps.Count; position++)
        {
            var step = steps[position];
            string number = Text(position + 1);

            #region 分组：要在可选的里面，首尾是 LoadPort

            var group = FindGroup(groups, step.Group);
            if (group is null)
            {
                return HandleResult.Fail(ErrorCodes.SequenceGroupNotFound, number, step.Group.Trim());
            }

            bool isEnd = position == 0 || position == steps.Count - 1;
            if (isEnd && !group.IsLoadPort)
            {
                return HandleResult.Fail(ErrorCodes.SequenceStepNotLoadPort, number);
            }

            #endregion

            #region 站点：至少勾一个，勾的都在这个分组里

            if (step.Stations.Count == 0)
            {
                return HandleResult.Fail(ErrorCodes.SequenceStationRequired, number);
            }

            foreach (string station in step.Stations)
            {
                if (!group.Modules.Contains(station.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    return HandleResult.Fail(ErrorCodes.SequenceStationNotInGroup, number, station.Trim(), group.Name);
                }
            }

            #endregion

            #region 工艺配方：要填、在库里、勾的每个站点都跑得了

            if (group.NeedsRecipe && string.IsNullOrWhiteSpace(step.Recipe))
            {
                return HandleResult.Fail(ErrorCodes.SequenceRecipeRequired, number);
            }

            if (group.NeedsRecipe && recipes is not null && !recipes.Contains(step.Recipe))
            {
                return HandleResult.Fail(ErrorCodes.SequenceRecipeNotFound, number, step.Recipe.Trim());
            }

            if (!group.NeedsRecipe || recipes is null)
            {
                continue;
            }

            foreach (string station in step.Stations)
            {
                var mismatch = recipes.FindMismatch(step.Recipe, station);
                if (mismatch is not null)
                {
                    var found = mismatch.Value;
                    return HandleResult.Fail(ErrorCodes.SequenceRecipeOptionMissing, number, station.Trim(), step.Recipe.Trim(), found.Field, found.Value);
                }
            }

            #endregion
        }

        return null;
    }

    /// <summary>
    /// 整理步骤
    /// </summary>
    /// <param name="steps"></param>
    /// <param name="groups"></param>
    /// <returns></returns>
    private static List<SequenceStep> Normalize(IReadOnlyList<SequenceStep> steps, IReadOnlyList<SequenceStationGroup> groups)
    {
        var result = new List<SequenceStep>();
        foreach (var step in steps)
        {
            var group = FindGroup(groups, step.Group)!;
            var picked = group.Modules.Where(module => step.Stations.Any(station => string.Equals(station.Trim(), module, StringComparison.OrdinalIgnoreCase)));
            result.Add(new SequenceStep(group.Name, picked, group.NeedsRecipe ? step.Recipe.Trim() : string.Empty));
        }

        return result;
    }

    private static SequenceStationGroup? FindGroup(IReadOnlyList<SequenceStationGroup> groups, string name)
    {
        return groups.FirstOrDefault(group => string.Equals(group.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string Text(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    #endregion
}
