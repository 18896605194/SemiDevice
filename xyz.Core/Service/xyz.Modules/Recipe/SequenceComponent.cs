using System.Globalization;
using System.Text.RegularExpressions;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Configs.Models;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Modules;

/// <summary>
/// 流程配方库（配方 → 流程配方页）：编号 1~Capacity，一个编号一个文件（Folder 下 001.xml、002.xml……）。
/// 流程配方说的是片在设备里怎么走：第 1 步从哪些 LoadPort 取片，中间按顺序经过哪些站点（同一步勾几个 = 哪个空去哪个）、
/// 每步跑哪个工艺配方，最后一步放回哪些 LoadPort（默认从哪来回哪去）。
/// 可选站点不写死：模块全起来后 Bind 一次，按 sc.xml 的分组节点（LoadPort、Chamber……）和下面装的模块生成。
/// 只有 gRPC 线程调它（设备扫描线程不碰），所以读写文件放在锁里也卡不到设备，还省得两次保存交叉写坏文件。
/// </summary>
[Component(description: "流程配方库：编号 1~N 的流程配方，一个编号一个文件")]
public class SequenceComponent : ComponentBase
{
    /// <summary>
    /// 当前流程配方库；sc.xml 里装出来即生效。冒烟与测试可以直接换成自己的实例。
    /// </summary>
    public static SequenceComponent? Current { get; set; }

    /// <summary>
    /// 文件名里编号的写法（001.xml）：只管写出来长什么样，读的时候按数字认，不靠位数。
    /// </summary>
    private const string FileNumberFormat = "D3";

    private const string FileExtension = ".xml";

    /// <summary>
    /// 先写到临时文件再换过去：写到一半断电，原来那个文件还是好的。
    /// </summary>
    private const string TempExtension = ".tmp";

    /// <summary>
    /// 最少几步：第 1 步取片、中间至少一步、最后一步放片。
    /// </summary>
    private const int MinSteps = 3;

    /// <summary>
    /// 名称只能用字母、数字、_ 和 -：Host 按名字选配方，文件里、日志里也不出怪字符。
    /// </summary>
    private static readonly Regex NameRule = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    private readonly object _gate = new();

    /// <summary>
    /// 编号 → 流程配方，按编号排好。
    /// </summary>
    private readonly SortedDictionary<int, SequenceData> _items = new();

    private IReadOnlyList<SequenceStationGroup> _groups = [];

    public SequenceComponent()
    {
        Current = this;
    }

    #region SC

    [SCEditor("99", "Sequence", "流程配方个数：编号 1~N")]
    public int Capacity { get; set; } = 99;

    [SCEditor("Recipe\\Sequence", "Sequence", "流程配方文件目录：相对宿主运行目录，也可以写绝对路径；一个编号一个文件")]
    public string Folder { get; set; } = "Recipe\\Sequence";

    [SCEditor("32", "Sequence", "流程配方名称最多几个字符")]
    public int NameMaxLength { get; set; } = 32;

    #endregion

    /// <summary>
    /// 内容变了（新建、改名、删除、保存），参数是编号。在锁外发。
    /// </summary>
    public event Action<int>? Changed;

    /// <summary>
    /// 可选的站点分组（Bind 之后才有），按 sc.xml 里的先后。
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
    /// 装配读完 SC：先查参数（配错了开机就报出来），再从目录把流程配方读进来。
    /// </summary>
    protected override void OnSettingLoaded(ModuleConfig setting)
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

    /// <summary>
    /// 从目录把流程配方全部读进内存（装配时调一次）。目录没有就建；文件名不是编号的、编号超出个数的、读不出来的跳过并记日志——
    /// 一个坏文件不该拖垮开机，也不该连累别的流程配方。
    /// </summary>
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
    /// 生成可选站点分组（装配完、模块起来之后调一次）。按 sc.xml 的分组节点（不带 Type 的节点，如 LoadPort、Chamber）和下面装的模块，
    /// 只留能放片的站点、并且有机械手的站点表配了它（机械手到不了的地方，写进流程也走不了）；顶层直接装的站点自己成一组。
    /// 分组节点装配时不生成组件、名字也不进组件路径，所以分组名只能从配置树里拿。
    /// </summary>
    public void Bind(IEnumerable<ModuleConfig> settings, IEnumerable<BaseModule> modules)
    {
        var table = new Dictionary<string, BaseModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            table[module.Name] = module;
        }

        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var robot in table.Values.OfType<IRobot>())
        {
            reachable.UnionWith(robot.Stations.Keys);
        }

        var groups = new List<SequenceStationGroup>();
        foreach (var node in settings)
        {
            IReadOnlyList<ModuleConfig> members = string.IsNullOrWhiteSpace(node.Type) ? node.Children : new List<ModuleConfig> { node };
            var stations = new List<BaseModule>();
            foreach (var member in members)
            {
                if (!string.IsNullOrWhiteSpace(member.Type)
                    && table.TryGetValue(member.Name, out var module)
                    && module is ITransferStation
                    && reachable.Contains(module.Name))
                {
                    stations.Add(module);
                }
            }

            if (stations.Count > 0)
            {
                groups.Add(new SequenceStationGroup(
                    node.Name,
                    stations.Select(station => station.Name).ToList(),
                    stations.All(station => station is BaseLoadPortModule),
                    stations.Any(station => station is BaseChamberModule)));
            }
        }

        lock (_gate)
        {
            _groups = groups;
        }

        LogHelper.Info(Name, groups.Count == 0
            ? "没有可选的站点分组（没有机械手，或机械手站点表里的站点都没装），流程配方编辑不了步骤"
            : $"可选站点分组 {groups.Count} 个：{string.Join("；", groups.Select(group => $"{group.Name}（{string.Join(", ", group.Modules)}）"))}");
    }

    /// <summary>
    /// 全部流程配方（副本），按编号从小到大。
    /// </summary>
    public IReadOnlyList<SequenceData> List()
    {
        lock (_gate)
        {
            return _items.Values.Select(item => item.Clone()).ToList();
        }
    }

    /// <summary>
    /// 按名字取一个流程配方的快照（副本，不分大小写、去掉首尾空白）；没有返回 null。
    /// 建 Job 时拿快照：之后库里改了、改名了、删了，已经建好的 Job 照原样跑。
    /// </summary>
    public SequenceData? Find(string name)
    {
        string wanted = name.Trim();
        lock (_gate)
        {
            return _items.Values.FirstOrDefault(item => string.Equals(item.Name, wanted, StringComparison.OrdinalIgnoreCase))?.Clone();
        }
    }

    /// <summary>
    /// 按编号取一个（副本）。
    /// </summary>
    public SequenceResult Get(int index)
    {
        lock (_gate)
        {
            return CheckExists(index) ?? SequenceResult.Ok(_items[index].Clone());
        }
    }

    /// <summary>
    /// 新建：空编号上建一个，名称要合规、不重名。内容按默认——LoadPort → 第一个要工艺配方的分组 → LoadPort，
    /// 模块全勾，工艺配方留空等人选（没选之前保存不了）。
    /// </summary>
    public SequenceResult Create(int index, string name, string operatorName)
    {
        name = name.Trim();
        SequenceResult result;
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

        Report(result, index, $"新建流程配方 {index} 号 {name}（操作人 {operatorName}）");
        return result;
    }

    /// <summary>
    /// 重命名：名称要合规、不跟别的编号重；版本加 1，记修改人、修改时间。
    /// </summary>
    public SequenceResult Rename(int index, string name, string operatorName)
    {
        name = name.Trim();
        SequenceResult result;
        lock (_gate)
        {
            result = CheckExists(index) ?? CheckName(name, index) ?? Store(Touch(_items[index], operatorName, next => next.Name = name));
        }

        Report(result, index, $"流程配方 {index} 号改名为 {name}（操作人 {operatorName}）");
        return result;
    }

    /// <summary>
    /// 删除：文件删掉、编号空出来。
    /// </summary>
    public SequenceResult Delete(int index, string operatorName)
    {
        SequenceResult result;
        string name = string.Empty;
        lock (_gate)
        {
            result = CheckExists(index) ?? Remove(index, out name);
        }

        Report(result, index, $"删除流程配方 {index} 号 {name}（操作人 {operatorName}）");
        return result;
    }

    /// <summary>
    /// 保存说明和步骤：打开时读到的版本要对得上，步骤要检查通过；存完版本加 1，记修改人、修改时间。
    /// 存之前把步骤规整一下：分组名、站点名按 sc 里的写法，站点去重、按 sc 里的先后排，不要工艺配方的分组把配方清掉。
    /// </summary>
    public SequenceResult Save(int index, int revision, string description, IReadOnlyList<SequenceStep> steps, string operatorName)
    {
        // 工艺配方要在工艺配方库里（没装库就不查）。库只被这边查、从不反过来调这边，两把锁不会互等
        var recipes = ProcessRecipeComponent.Current;
        SequenceResult result;
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

        Report(result, index, $"保存流程配方 {index} 号（操作人 {operatorName}）");
        return result;
    }

    /// <summary>
    /// 成了记一条日志、发变更事件（锁外）；没成不记（原因回给界面，写文件失败的在写的地方已经记过）。
    /// </summary>
    private void Report(SequenceResult result, int index, string message)
    {
        if (!result.IsOk)
        {
            return;
        }

        var saved = result.Sequence;
        LogHelper.Info(Name, saved is null ? message : $"{message}，版本 {saved.Revision}");
        Changed?.Invoke(index);
    }

    /// <summary>
    /// 默认步骤（新建用）：LoadPort 全勾 → 第一个要工艺配方的分组全勾（没有就第一个不是 LoadPort 的分组）→ LoadPort 全勾。
    /// 没有站点分组（没绑、或 sc 里没有能用的站点）时是空的，等配好了再编。
    /// </summary>
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

    /// <summary>
    /// 写文件（先写临时文件再换过去），成了才换进内存；写不进去内存里的不动，把原因回给界面。必须在 _gate 里调。
    /// </summary>
    private SequenceResult Store(SequenceData data)
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
            return SequenceResult.Fail(ErrorCodes.SequenceSaveFailed, Text(data.Index), exception.Message);
        }

        _items[data.Index] = data;
        return SequenceResult.Ok(data.Clone());
    }

    /// <summary>
    /// 删文件，成了才从内存里拿掉。必须在 _gate 里调。
    /// </summary>
    private SequenceResult Remove(int index, out string name)
    {
        name = _items[index].Name;
        try
        {
            File.Delete(FilePath(index));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogHelper.Error(Name, $"{index} 号流程配方文件删不掉，库里的没动：{exception.Message}");
            return SequenceResult.Fail(ErrorCodes.SequenceSaveFailed, Text(index), exception.Message);
        }

        _items.Remove(index);
        return SequenceResult.Ok();
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

    private SequenceResult? CheckIndex(int index)
    {
        return index < 1 || index > Capacity
            ? SequenceResult.Fail(ErrorCodes.SequenceIndexOutOfRange, Text(index), Text(Capacity))
            : null;
    }

    private SequenceResult? CheckExists(int index)
    {
        return CheckIndex(index) ?? (_items.ContainsKey(index) ? null : SequenceResult.Fail(ErrorCodes.SequenceNotFound, Text(index)));
    }

    private SequenceResult? CheckFree(int index)
    {
        return _items.TryGetValue(index, out var existing)
            ? SequenceResult.Fail(ErrorCodes.SequenceIndexOccupied, Text(index), existing.Name)
            : null;
    }

    private SequenceResult? CheckRevision(int index, int revision)
    {
        return _items[index].Revision == revision ? null : SequenceResult.Fail(ErrorCodes.SequenceRevisionMismatch, Text(index));
    }

    /// <summary>
    /// 名称：必填、不超长、只用字母数字 _ -、不跟别的编号重（不分大小写）。
    /// </summary>
    private SequenceResult? CheckName(string name, int selfIndex)
    {
        if (name.Length == 0)
        {
            return SequenceResult.Fail(ErrorCodes.SequenceNameRequired);
        }

        if (name.Length > NameMaxLength)
        {
            return SequenceResult.Fail(ErrorCodes.SequenceNameTooLong, Text(NameMaxLength));
        }

        if (!NameRule.IsMatch(name))
        {
            return SequenceResult.Fail(ErrorCodes.SequenceNameInvalid, name);
        }

        foreach (var pair in _items)
        {
            if (pair.Key != selfIndex && string.Equals(pair.Value.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return SequenceResult.Fail(ErrorCodes.SequenceNameDuplicate, name, Text(pair.Key));
            }
        }

        return null;
    }

    /// <summary>
    /// 步骤：至少 3 步；第 1 步、最后一步是 LoadPort 分组；每步的分组要在可选分组里、至少勾一个站点、勾的站点在这个分组里、
    /// 要工艺配方的分组得填配方，装了工艺配方库时配方还得在库里，而且配方里下拉选的（摆臂、药液这类从腔体部件取的）勾的每个腔体都有
    /// （几个腔体装的不一样时才会对不上）。步号从 1 数，跟界面上一样。
    /// </summary>
    private static SequenceResult? CheckSteps(IReadOnlyList<SequenceStep> steps, IReadOnlyList<SequenceStationGroup> groups, ProcessRecipeComponent? recipes)
    {
        if (steps.Count < MinSteps)
        {
            return SequenceResult.Fail(ErrorCodes.SequenceTooFewSteps);
        }

        for (int position = 0; position < steps.Count; position++)
        {
            var step = steps[position];
            string number = Text(position + 1);
            var group = FindGroup(groups, step.Group);
            if (group is null)
            {
                return SequenceResult.Fail(ErrorCodes.SequenceGroupNotFound, number, step.Group.Trim());
            }

            bool isEnd = position == 0 || position == steps.Count - 1;
            if (isEnd && !group.IsLoadPort)
            {
                return SequenceResult.Fail(ErrorCodes.SequenceStepNotLoadPort, number);
            }

            if (step.Stations.Count == 0)
            {
                return SequenceResult.Fail(ErrorCodes.SequenceStationRequired, number);
            }

            foreach (string station in step.Stations)
            {
                if (!group.Modules.Contains(station.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    return SequenceResult.Fail(ErrorCodes.SequenceStationNotInGroup, number, station.Trim(), group.Name);
                }
            }

            if (group.NeedsRecipe && string.IsNullOrWhiteSpace(step.Recipe))
            {
                return SequenceResult.Fail(ErrorCodes.SequenceRecipeRequired, number);
            }

            if (group.NeedsRecipe && recipes is not null && !recipes.Contains(step.Recipe))
            {
                return SequenceResult.Fail(ErrorCodes.SequenceRecipeNotFound, number, step.Recipe.Trim());
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
                    return SequenceResult.Fail(ErrorCodes.SequenceRecipeOptionMissing, number, station.Trim(), step.Recipe.Trim(), mismatch.Field, mismatch.Value);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 规整步骤（检查通过之后调）：分组名、站点名换成 sc 里的写法，站点去重、按 sc 里的先后排；配方去掉首尾空白，不要配方的分组清空。
    /// </summary>
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

    private string FolderPath()
    {
        return System.IO.Path.IsPathRooted(Folder) ? Folder : System.IO.Path.Combine(AppContext.BaseDirectory, Folder);
    }

    private string FilePath(int index)
    {
        return System.IO.Path.Combine(FolderPath(), index.ToString(FileNumberFormat, CultureInfo.InvariantCulture) + FileExtension);
    }

    private static string Text(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
