using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
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

/// <summary>
/// 工艺配方库（配方 → 工艺配方页）：编号 1~Capacity，一个编号一个文件（Folder 下 001.xml、002.xml……）。
/// 工艺配方说的是片进了腔体以后一步一步怎么做。每一步有哪些字段不写死，按 sc.xml 本节点下 Fields 的字段表来
/// （一个子节点一个字段：类型、上下限、默认值、下拉的数据源……），界面按它生成步骤表，这里按它查、按它存。
/// 下拉从腔体部件取的选项在模块全起来后 Bind 一次，按每个腔体装的部件取；几个腔体装的不一样时给界面合起来的，
/// 用到具体腔体（流程配方、腔体起工艺）时再按那个腔体查（<see cref="FindMismatch"/>）。
/// 合计时长的上限跟腔体的工艺超时（EC，现查）走。流程配方的工艺步骤、腔体起工艺都按名字引用这里的配方。
/// 只有 gRPC 线程和 EAP（Host 远程管配方，经 IProcessRecipeComponent）调它（设备扫描线程不碰），所以读写文件放在锁里也卡不到设备，还省得两次保存交叉写坏文件。
/// </summary>
[Component(description: "工艺配方库：编号 1~N 的工艺配方，一个编号一个文件；每一步的字段按下面 Fields 的字段表")]
public class ProcessRecipeComponent : ComponentBase, IProcessRecipeComponent
{
    /// <summary>
    /// 当前工艺配方库；sc.xml 里装出来即生效。冒烟与测试可以直接换成自己的实例。
    /// </summary>
    public static ProcessRecipeComponent? Current { get; set; }

    /// <summary>
    /// 字段表在本节点下的分组名：一个子节点一个字段，节点名就是字段名。
    /// </summary>
    public const string FieldsNodeName = "Fields";

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
    /// 腔体工艺超时（EC）是毫秒，合计时长按秒比。
    /// </summary>
    private const double MillisecondsPerSecond = 1000;

    /// <summary>
    /// 错误提示里数字的写法：最多两位小数，不带多余的 0。
    /// </summary>
    private const string NumberFormat = "0.##";

    /// <summary>
    /// 名称只能用字母、数字、_ 和 -：Host、流程配方按名字选配方，文件里、日志里也不出怪字符。
    /// </summary>
    private static readonly Regex NameRule = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, ProcessRecipeChoices> NoChoices =
        new Dictionary<string, ProcessRecipeChoices>(StringComparer.OrdinalIgnoreCase);

    private readonly object _gate = new();

    /// <summary>
    /// 编号 → 工艺配方，按编号排好。
    /// </summary>
    private readonly SortedDictionary<int, ProcessRecipeData> _items = new();

    /// <summary>
    /// 字段表，按 sc.xml 里的先后（就是步骤表从左到右）。
    /// </summary>
    private IReadOnlyList<ProcessRecipeField> _fields = [];

    /// <summary>
    /// 从腔体部件取选项的下拉：字段名 → 所有腔体合起来能选的。
    /// </summary>
    private IReadOnlyDictionary<string, ProcessRecipeChoices> _choices = NoChoices;

    /// <summary>
    /// 腔体名 → （字段名 → 这个腔体能选的）：配方用到具体腔体时按它查。
    /// </summary>
    private IReadOnlyDictionary<string, IReadOnlyDictionary<string, ProcessRecipeChoices>> _chamberChoices =
        new Dictionary<string, IReadOnlyDictionary<string, ProcessRecipeChoices>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 装起来的腔体：合计时长的上限按它们的工艺超时现查（EC 在线能改）。
    /// </summary>
    private IReadOnlyList<BaseChamberModule> _chambers = [];

    public ProcessRecipeComponent()
    {
        Current = this;
    }

    #region SC

    [SCEditor("99", "ProcessRecipe", "工艺配方个数：编号 1~N")]
    public int Capacity { get; set; } = 99;

    [SCEditor("Recipe\\Process", "ProcessRecipe", "工艺配方文件目录：相对宿主运行目录，也可以写绝对路径；一个编号一个文件")]
    public string Folder { get; set; } = "Recipe\\Process";

    [SCEditor("32", "ProcessRecipe", "工艺配方名称最多几个字符")]
    public int NameMaxLength { get; set; } = 32;

    #endregion

    /// <summary>
    /// 内容变了（新建、改名、删除、保存），参数是编号。在锁外发。
    /// </summary>
    public event Action<int>? Changed;

    /// <summary>
    /// 字段表（装配时从 sc.xml 读的，之后不变）。
    /// </summary>
    public IReadOnlyList<ProcessRecipeField> Fields
    {
        get
        {
            lock (_gate)
            {
                return _fields;
            }
        }
    }

    /// <summary>
    /// 合计时长上限（秒）：装起来的腔体里工艺超时最短的那个（现查 EC）；没有腔体时为 0（不限）。
    /// </summary>
    public double MaxTotalSeconds
    {
        get
        {
            IReadOnlyList<BaseChamberModule> chambers;
            lock (_gate)
            {
                chambers = _chambers;
            }

            return chambers.Count == 0 ? 0 : chambers.Min(chamber => chamber.ProcessTimeout) / MillisecondsPerSecond;
        }
    }

    /// <summary>
    /// 装配读完 SC：先查参数和字段表（配错了开机就报出来），再从目录把工艺配方读进来。
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
            throw new InvalidOperationException($"sc.xml 节点 {Name} 没配 Folder（工艺配方文件放哪）");
        }

        var fields = ReadFields(setting);
        lock (_gate)
        {
            _fields = fields;
        }

        Load();
    }

    /// <summary>
    /// 读字段表：字段名不能重复；必须有 Seconds（步骤时长，小数）；数据源里 @ 跟的字段要在表里，
    /// 而且是从腔体部件取名字的下拉（这样"它选中的部件下面"才说得通）。
    /// </summary>
    private static List<ProcessRecipeField> ReadFields(ModuleConfig setting)
    {
        var node = setting.Children.FirstOrDefault(child => string.Equals(child.Name, FieldsNodeName, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            throw new InvalidOperationException($"sc.xml 节点 {setting.Name} 下没配 {FieldsNodeName}（工艺配方每一步有哪些字段，一个子节点一个）");
        }

        var fields = new List<ProcessRecipeField>();
        foreach (var fieldNode in node.Children)
        {
            string path = $"{setting.Name}.{FieldsNodeName}.{fieldNode.Name}";
            var field = ProcessRecipeField.FromConfig(fieldNode, path);
            if (FindField(fields, field.Key) is not null)
            {
                throw new InvalidOperationException($"sc.xml 节点 {path}：字段名 {field.Key} 重复了");
            }

            fields.Add(field);
        }

        var seconds = FindField(fields, ProcessRecipeField.SecondsKey);
        if (seconds is null || seconds.Type != ProcessRecipeFieldType.Double)
        {
            throw new InvalidOperationException(
                $"sc.xml 节点 {setting.Name}.{FieldsNodeName} 里要有 {ProcessRecipeField.SecondsKey}（步骤时长，Type 为 Double）：合计时长和工艺超时靠它");
        }

        foreach (var field in fields)
        {
            var source = field.Source;
            if (source is null || source.ParentKey.Length == 0)
            {
                continue;
            }

            var parent = FindField(fields, source.ParentKey);
            var parentSource = parent?.Source;
            if (parent is null || ReferenceEquals(parent, field) || parentSource is null || !parentSource.IsParts || parentSource.Property.Length > 0)
            {
                throw new InvalidOperationException(
                    $"sc.xml 节点 {setting.Name}.{FieldsNodeName}.{field.Key} 的 Source=\"{source.Text}\"：@ 后面要是表里别的、从腔体部件取名字的下拉字段（Parts:类型，不带属性）");
            }
        }

        return fields;
    }

    private static ProcessRecipeField? FindField(IEnumerable<ProcessRecipeField> fields, string key)
    {
        return fields.FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 从目录把工艺配方全部读进内存（装配时调一次）。目录没有就建；文件名不是编号的、编号超出个数的、读不出来的跳过并记日志——
    /// 一个坏文件不该拖垮开机，也不该连累别的工艺配方。字段表里有、文件里没有的字段按默认值补上（字段表后来加的）。
    /// </summary>
    public void Load()
    {
        string folder = FolderPath();
        var fields = Fields;
        var loaded = new SortedDictionary<int, ProcessRecipeData>();
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

                foreach (var step in data.Steps)
                {
                    FillDefaults(step, fields);
                }

                if (!loaded.TryAdd(data.Index, data))
                {
                    LogHelper.Warn(Name, $"跳过 {file}：{data.Index} 号已经有一个文件了（文件名写法不同但编号一样）");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogHelper.Error(Name, $"工艺配方目录打不开：{folder}（{exception.Message}），列表先是空的");
        }

        lock (_gate)
        {
            _items.Clear();
            foreach (var pair in loaded)
            {
                _items[pair.Key] = pair.Value;
            }
        }

        LogHelper.Info(Name, $"读入工艺配方 {loaded.Count} 个（目录 {folder}）");
    }

    /// <summary>
    /// 取下拉从腔体部件来的选项（装配完、模块起来之后调一次）：按每个腔体下装的部件取，几个腔体合起来给界面；
    /// 每个腔体自己的也记下来，配方用到具体腔体时按它查。腔体也记下来，合计时长的上限按它们的工艺超时现查。
    /// 数据源里写的属性部件上没有（sc.xml 写错了）就抛，开机就报出来。
    /// </summary>
    public void Bind(IEnumerable<BaseModule> modules)
    {
        var chambers = modules.OfType<BaseChamberModule>().ToList();
        var fields = Fields;
        var partFields = fields.Where(field => field.Source is not null && field.Source.IsParts).ToList();
        var perChamber = new Dictionary<string, IReadOnlyDictionary<string, ProcessRecipeChoices>>(StringComparer.OrdinalIgnoreCase);
        foreach (var chamber in chambers)
        {
            var byField = new Dictionary<string, ProcessRecipeChoices>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in partFields)
            {
                byField[field.Key] = Collect(chamber, field.Source!, fields);
            }

            perChamber[chamber.Name] = byField;
        }

        var merged = partFields.ToDictionary(
            field => field.Key,
            field => ProcessRecipeChoices.Merge(perChamber.Values.Select(byField => byField[field.Key])),
            StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            _chambers = chambers;
            _chamberChoices = perChamber;
            _choices = merged;
        }

        foreach (var field in partFields)
        {
            var choices = merged[field.Key];
            string text = field.Source!.ParentKey.Length == 0
                ? string.Join(", ", choices.Values)
                : string.Join("；", choices.ByParent.Select(pair => $"{pair.Key}：{string.Join(", ", pair.Value)}"));
            LogHelper.Info(Name, $"字段 {field.Key} 能选的（{field.Source.Text}）：{(text.Length == 0 ? "没有" : text)}");
            if (field.Default.Length > 0 && field.Source.ParentKey.Length == 0 && !choices.Values.Contains(field.Default, StringComparer.OrdinalIgnoreCase))
            {
                LogHelper.Warn(Name, $"字段 {field.Key} 的默认值 {field.Default} 在腔体部件里找不到，新加的步骤这一格会报错");
            }
        }
    }

    /// <summary>
    /// 一个腔体里，一个从部件取选项的下拉能选的：不跟别的字段走的在整个腔体里找；
    /// 跟着别的字段走的，按那个字段能选的每个部件，在它下面找。
    /// </summary>
    private static ProcessRecipeChoices Collect(BaseChamberModule chamber, ProcessRecipeSource source, IReadOnlyList<ProcessRecipeField> fields)
    {
        if (source.ParentKey.Length == 0)
        {
            return new ProcessRecipeChoices(ValuesUnder(chamber, source), ProcessRecipeChoices.Empty.ByParent);
        }

        var parentSource = FindField(fields, source.ParentKey)!.Source!;
        var byParent = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in PartsUnder(chamber, parentSource.PartType))
        {
            byParent.TryAdd(part.Name, ValuesUnder(part, source));
        }

        return new ProcessRecipeChoices([], byParent);
    }

    /// <summary>
    /// root 下面（不含自己）这种类型的部件，按 sc.xml 里的先后。类型按类名认，基类名也算（机型自己派生的喷嘴也是喷嘴）。
    /// </summary>
    private static IEnumerable<ComponentBase> PartsUnder(ComponentBase root, string typeName)
    {
        foreach (var child in root.Children)
        {
            if (IsOfType(child, typeName))
            {
                yield return child;
            }

            foreach (var inner in PartsUnder(child, typeName))
            {
                yield return inner;
            }
        }
    }

    private static bool IsOfType(ComponentBase component, string typeName)
    {
        for (var type = component.GetType(); type is not null; type = type.BaseType)
        {
            if (string.Equals(type.Name, typeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// root 下面这种部件的名字（或属性的值），去掉空的、重复的。
    /// </summary>
    private static List<string> ValuesUnder(ComponentBase root, ProcessRecipeSource source)
    {
        var values = new List<string>();
        foreach (var part in PartsUnder(root, source.PartType))
        {
            string value = source.Property.Length == 0 ? part.Name : PropertyText(part, source);
            if (value.Length > 0)
            {
                ProcessRecipeChoices.AddDistinct(values, [value]);
            }
        }

        return values;
    }

    private static string PropertyText(ComponentBase part, ProcessRecipeSource source)
    {
        var property = part.GetType().GetProperty(source.Property, BindingFlags.Public | BindingFlags.Instance);
        if (property is null)
        {
            throw new InvalidOperationException($"sc.xml 工艺配方字段的数据源 {source.Text}：部件 {part.FullPath} 没有属性 {source.Property}");
        }

        return (property.GetValue(part)?.ToString() ?? string.Empty).Trim();
    }

    /// <summary>
    /// 一个下拉字段能选的（所有腔体合起来）：直接写选项的就是写的那些；从部件取的按 Bind 取到的；不是下拉的为空。
    /// </summary>
    public ProcessRecipeChoices ChoicesOf(ProcessRecipeField field)
    {
        lock (_gate)
        {
            return ChoicesOf(field, _choices);
        }
    }

    private static ProcessRecipeChoices ChoicesOf(ProcessRecipeField field, IReadOnlyDictionary<string, ProcessRecipeChoices> parts)
    {
        var source = field.Source;
        if (source is null)
        {
            return ProcessRecipeChoices.Empty;
        }

        if (!source.IsParts)
        {
            return new ProcessRecipeChoices(source.Options, ProcessRecipeChoices.Empty.ByParent);
        }

        return parts.TryGetValue(field.Key, out var choices) ? choices : ProcessRecipeChoices.Empty;
    }

    /// <summary>
    /// 全部工艺配方（副本），按编号从小到大。
    /// </summary>
    public IReadOnlyList<ProcessRecipeData> List()
    {
        lock (_gate)
        {
            return _items.Values.Select(item => item.Clone()).ToList();
        }
    }

    /// <summary>
    /// 按编号取一个（副本）。
    /// </summary>
    public ProcessRecipeResult Get(int index)
    {
        lock (_gate)
        {
            return CheckExists(index) ?? ProcessRecipeResult.Ok(_items[index].Clone());
        }
    }

    /// <summary>
    /// 库里有没有这个名字的工艺配方（不分大小写、去掉首尾空白）：流程配方保存、腔体起工艺时用。
    /// </summary>
    public bool Contains(string name)
    {
        string wanted = name.Trim();
        lock (_gate)
        {
            return _items.Values.Any(item => string.Equals(item.Name, wanted, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// 按名字取一个工艺配方的快照（副本，不分大小写、去掉首尾空白）；没有返回 null。
    /// Job 建 PJ、腔体起工艺都拿快照：之后库里改了、删了，已经拿到的那一份不变。
    /// </summary>
    public ProcessRecipeData? Find(string name)
    {
        lock (_gate)
        {
            return FindByName(name.Trim())?.Clone();
        }
    }

    /// <summary>
    /// 这个配方用在这个腔体上对不对得上：配方里从腔体部件取选项的下拉，选的值这个腔体有没有（几个腔体装的不一样时才会对不上）。
    /// 对得上、腔体不认识（不是腔体，或没绑上）、配方不在库里（那是另一个错，由 <see cref="Contains"/> 那一步报）都返回 null。
    /// </summary>
    public ProcessRecipeMismatch? FindMismatch(string recipeName, string chamber)
    {
        string wanted = recipeName.Trim();
        ProcessRecipeData? recipe;
        lock (_gate)
        {
            recipe = _items.Values.FirstOrDefault(item => string.Equals(item.Name, wanted, StringComparison.OrdinalIgnoreCase));
        }

        return recipe is null ? null : FindMismatch(recipe, chamber);
    }

    /// <summary>
    /// 同上，查的是一份配方内容（快照）：Job、起工艺拿着快照查，不再按名字回库里找——库里的可能已经改了。
    /// </summary>
    public ProcessRecipeMismatch? FindMismatch(ProcessRecipeData recipe, string chamber)
    {
        lock (_gate)
        {
            if (!_chamberChoices.TryGetValue(chamber.Trim(), out var choices))
            {
                return null;
            }

            string? language = SystemComponent.Current?.Language;
            foreach (var step in recipe.Steps)
            {
                foreach (var field in _fields)
                {
                    var source = field.Source;
                    string value = step.Get(field.Key).Trim();
                    if (source is null || !source.IsParts || value.Length == 0)
                    {
                        continue;
                    }

                    var options = (choices.TryGetValue(field.Key, out var found) ? found : ProcessRecipeChoices.Empty).For(source.ParentKey, step);
                    if (!options.Contains(value, StringComparer.OrdinalIgnoreCase))
                    {
                        return new ProcessRecipeMismatch(field.DisplayText(language), value);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 新建：空编号上建一个，名称要合规、不重名。内容先给一步（各字段的默认值），人再改。
    /// </summary>
    public ProcessRecipeResult Create(int index, string name, string operatorName)
    {
        name = name.Trim();
        ProcessRecipeResult result;
        lock (_gate)
        {
            var now = DateTime.Now;
            result = CheckIndex(index) ?? CheckFree(index) ?? CheckName(name, index) ?? Store(new ProcessRecipeData
            {
                Index = index,
                Name = name,
                CreatedBy = operatorName,
                CreatedAt = now,
                ModifiedBy = operatorName,
                ModifiedAt = now,
                Revision = 1,
                Steps = [NewStep(_fields)],
            });
        }

        Report(result, index, $"新建工艺配方 {index} 号 {name}（操作人 {operatorName}）", (name, ChangeKind.Created));
        return result;
    }

    /// <summary>
    /// 重命名：名称要合规、不跟别的编号重；版本加 1，记修改人、修改时间。
    /// </summary>
    public ProcessRecipeResult Rename(int index, string name, string operatorName)
    {
        name = name.Trim();
        ProcessRecipeResult result;
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
        Report(result, index, $"工艺配方 {index} 号改名为 {name}（操作人 {operatorName}）", (oldName, ChangeKind.Deleted), (name, ChangeKind.Created));
        return result;
    }

    /// <summary>
    /// 删除：文件删掉、编号空出来。
    /// </summary>
    public ProcessRecipeResult Delete(int index, string operatorName)
    {
        ProcessRecipeResult result;
        string name = string.Empty;
        lock (_gate)
        {
            result = CheckExists(index) ?? Remove(index, out name);
        }

        Report(result, index, $"删除工艺配方 {index} 号 {name}（操作人 {operatorName}）", (name, ChangeKind.Deleted));
        return result;
    }

    /// <summary>
    /// 保存说明和步骤：打开时读到的版本要对得上，步骤要按字段表检查通过；存完版本加 1，记修改人、修改时间。
    /// 存之前把步骤规整一下：只留字段表里的字段、按字段表的先后，数字、开关换成统一写法，下拉换成数据源里的写法。
    /// </summary>
    public ProcessRecipeResult Save(int index, int revision, string description, IReadOnlyList<ProcessRecipeStep> steps, string operatorName)
    {
        double maxTotal = MaxTotalSeconds;
        string? language = SystemComponent.Current?.Language;
        ProcessRecipeResult result;
        lock (_gate)
        {
            var fields = _fields;
            var choices = _choices;
            result = CheckExists(index)
                ?? CheckRevision(index, revision)
                ?? CheckSteps(steps, fields, choices, maxTotal, language)
                ?? Store(Touch(_items[index], operatorName, next =>
                {
                    next.Description = description.Trim();
                    next.Steps = Normalize(steps, fields, choices);
                }));
        }

        Report(result, index, $"保存工艺配方 {index} 号（操作人 {operatorName}）", (result.Recipe?.Name ?? string.Empty, ChangeKind.Edited));
        return result;
    }

    /// <summary>
    /// 成了记一条日志、发变更事件（锁外），接着 EAP 的话把配方怎么变的报给 Host（放进 EAP 的派发组件）；
    /// 没成不记（原因回给界面，写文件失败的在写的地方已经记过）。
    /// </summary>
    private void Report(ProcessRecipeResult result, int index, string message, params (string Name, ChangeKind Change)[] changes)
    {
        if (!result.IsOk)
        {
            return;
        }

        var saved = result.Recipe;
        LogHelper.Info(Name, saved is null ? message : $"{message}，版本 {saved.Revision}");
        Changed?.Invoke(index);

        var callback = E30Callback;
        if (callback is null)
        {
            return;
        }

        foreach (var (name, change) in changes)
        {
            EapNotifierComponent.Current?.Post(() => callback.ProcessRecipeChanged(name, change));
        }
    }

    #region EAP 口子（IProcessRecipeComponent：Host 远程按名字列、取、存、删，跟本地过同一套检查）

    /// <summary>上报口：配方建、改、删了经 EAP 的派发组件报给 Host；null = 没接 EAP。</summary>
    public IE30Callback? E30Callback { get; set; }

    /// <summary>全部工艺配方的名字，按编号。</summary>
    public IReadOnlyList<string> ProcessRecipeNames
    {
        get
        {
            lock (_gate)
            {
                return _items.Values.Select(item => item.Name).ToList();
            }
        }
    }

    /// <summary>一个工艺配方转成 JSON（库里那份原样，每一步是字段名 → 值）；没有返回 null。</summary>
    public string? ExportProcessRecipe(string name)
    {
        var found = Find(name);
        return found is null ? null : JsonHelper.Serialize(found);
    }

    /// <summary>JSON 看样子是不是工艺配方：每一步都带 values（字段名 → 值）。</summary>
    public bool AcceptsProcessRecipe(string json)
    {
        return JsonSteps.AllHave(json, "values");
    }

    /// <summary>
    /// Host 下的工艺配方：库里没有就在第一个空编号上新建（版本 1），有就把说明和步骤整个换掉（版本加 1）；
    /// 名字、每一步的检查（按字段表、合计时长）跟本地新建、保存一样，JSON 里的编号、版本、建 / 改的人和时间不管。
    /// </summary>
    public HandleResult ImportProcessRecipe(string name, string json, string operatorName)
    {
        name = name.Trim();
        var body = ParseBody(json);
        if (body is null)
        {
            return HandleResult.Fail(ErrorCodes.ProcessRecipeBodyInvalid, name);
        }

        double maxTotal = MaxTotalSeconds;
        string? language = SystemComponent.Current?.Language;
        ProcessRecipeResult result;
        int index;
        string reported;
        bool created;
        lock (_gate)
        {
            var fields = _fields;
            var choices = _choices;
            var existing = FindByName(name);
            created = existing is null;
            if (existing is null)
            {
                index = FirstFreeIndex();
                reported = name;
                var now = DateTime.Now;
                result = (index == 0 ? ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeFull, Text(Capacity)) : null)
                    ?? CheckName(name, index)
                    ?? CheckSteps(body.Steps, fields, choices, maxTotal, language)
                    ?? Store(new ProcessRecipeData
                    {
                        Index = index,
                        Name = name,
                        Description = body.Description.Trim(),
                        CreatedBy = operatorName,
                        CreatedAt = now,
                        ModifiedBy = operatorName,
                        ModifiedAt = now,
                        Revision = 1,
                        Steps = Normalize(body.Steps, fields, choices),
                    });
            }
            else
            {
                index = existing.Index;
                reported = existing.Name;
                result = CheckSteps(body.Steps, fields, choices, maxTotal, language) ?? Store(Touch(existing, operatorName, next =>
                {
                    next.Description = body.Description.Trim();
                    next.Steps = Normalize(body.Steps, fields, choices);
                }));
            }
        }

        Report(result, index, $"{(created ? "远程新建" : "远程覆盖")}工艺配方 {index} 号 {reported}（操作人 {operatorName}）",
            (reported, created ? ChangeKind.Created : ChangeKind.Edited));
        return ToHandleResult(result);
    }

    /// <summary>按名字删一个工艺配方（Host 删）。</summary>
    public HandleResult DeleteProcessRecipe(string name, string operatorName)
    {
        name = name.Trim();
        ProcessRecipeResult result;
        int index;
        string removed;
        lock (_gate)
        {
            var existing = FindByName(name);
            if (existing is null)
            {
                return HandleResult.Fail(ErrorCodes.ProcessRecipeNameNotFound, name);
            }

            index = existing.Index;
            result = Remove(index, out removed);
        }

        Report(result, index, $"远程删除工艺配方 {index} 号 {removed}（操作人 {operatorName}）", (removed, ChangeKind.Deleted));
        return ToHandleResult(result);
    }

    /// <summary>
    /// Host 给的 JSON 转成工艺配方（只用说明和步骤）；读不出来返回 null。空着的说明、步骤、字段名、值补成空的，后面按字段表照常查。
    /// </summary>
    private static ProcessRecipeData? ParseBody(string json)
    {
        ProcessRecipeData? body;
        try
        {
            body = JsonHelper.Deserialize<ProcessRecipeData>(json);
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
            .Select(step => new ProcessRecipeStep
            {
                Values = (step.Values ?? [])
                    .Where(value => value is not null)
                    .Select(value => new ProcessRecipeValue(value.Name ?? string.Empty, value.Value ?? string.Empty))
                    .ToList(),
            })
            .ToList();
        return body;
    }

    private static HandleResult ToHandleResult(ProcessRecipeResult result)
    {
        return result.IsOk ? HandleResult.Success() : HandleResult.Fail(result.Code, [.. result.Args]);
    }

    #endregion

    /// <summary>按名字找（不分大小写）；必须在 _gate 里调。</summary>
    private ProcessRecipeData? FindByName(string name)
    {
        return _items.Values.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>第一个空着的编号；都用了返回 0。必须在 _gate 里调。</summary>
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
    /// 改一个工艺配方：在副本上改，版本加 1，记修改人、修改时间；写文件成了才换进库里。
    /// </summary>
    private static ProcessRecipeData Touch(ProcessRecipeData current, string operatorName, Action<ProcessRecipeData> change)
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
    private ProcessRecipeResult Store(ProcessRecipeData data)
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
            LogHelper.Error(Name, $"{data.Index} 号工艺配方写文件失败，库里的没改：{exception.Message}");
            DeleteTemp(temp);
            return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeSaveFailed, Text(data.Index), exception.Message);
        }

        _items[data.Index] = data;
        return ProcessRecipeResult.Ok(data.Clone());
    }

    /// <summary>
    /// 删文件，成了才从内存里拿掉。必须在 _gate 里调。
    /// </summary>
    private ProcessRecipeResult Remove(int index, out string name)
    {
        name = _items[index].Name;
        try
        {
            File.Delete(FilePath(index));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogHelper.Error(Name, $"{index} 号工艺配方文件删不掉，库里的没动：{exception.Message}");
            return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeSaveFailed, Text(index), exception.Message);
        }

        _items.Remove(index);
        return ProcessRecipeResult.Ok();
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
    private ProcessRecipeData? ReadFile(string file)
    {
        if (!int.TryParse(System.IO.Path.GetFileNameWithoutExtension(file), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            || index < 1 || index > Capacity)
        {
            LogHelper.Warn(Name, $"跳过 {file}：文件名不是 1~{Capacity} 的编号");
            return null;
        }

        try
        {
            var data = XmlHelper.Deserialize<ProcessRecipeData>(file);
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

    /// <summary>
    /// 字段表里有、这一步里没有的字段（字段表后来加的）按默认值补上；这一步里有、字段表里没有的（后来删了）先留着，保存时丢掉。
    /// </summary>
    private static void FillDefaults(ProcessRecipeStep step, IReadOnlyList<ProcessRecipeField> fields)
    {
        foreach (var field in fields)
        {
            if (!step.Has(field.Key))
            {
                step.Values.Add(new ProcessRecipeValue(field.Key, field.Default));
            }
        }
    }

    /// <summary>
    /// 新加的一步：每个字段填默认值。
    /// </summary>
    private static ProcessRecipeStep NewStep(IReadOnlyList<ProcessRecipeField> fields)
    {
        return new ProcessRecipeStep { Values = fields.Select(field => new ProcessRecipeValue(field.Key, field.Default)).ToList() };
    }

    private ProcessRecipeResult? CheckIndex(int index)
    {
        return index < 1 || index > Capacity
            ? ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeIndexOutOfRange, Text(index), Text(Capacity))
            : null;
    }

    private ProcessRecipeResult? CheckExists(int index)
    {
        return CheckIndex(index) ?? (_items.ContainsKey(index) ? null : ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeNotFound, Text(index)));
    }

    private ProcessRecipeResult? CheckFree(int index)
    {
        return _items.TryGetValue(index, out var existing)
            ? ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeIndexOccupied, Text(index), existing.Name)
            : null;
    }

    private ProcessRecipeResult? CheckRevision(int index, int revision)
    {
        return _items[index].Revision == revision ? null : ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeRevisionMismatch, Text(index));
    }

    /// <summary>
    /// 名称：必填、不超长、只用字母数字 _ -、不跟别的编号重（不分大小写）。
    /// </summary>
    private ProcessRecipeResult? CheckName(string name, int selfIndex)
    {
        if (name.Length == 0)
        {
            return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeNameRequired);
        }

        if (name.Length > NameMaxLength)
        {
            return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeNameTooLong, Text(NameMaxLength));
        }

        if (!NameRule.IsMatch(name))
        {
            return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeNameInvalid, name);
        }

        foreach (var pair in _items)
        {
            if (pair.Key != selfIndex && string.Equals(pair.Value.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeNameDuplicate, name, Text(pair.Key));
            }
        }

        return null;
    }

    /// <summary>
    /// 步骤：至少一步；每一步每个字段按字段表查——必填的不能空，整数、小数的写法、上下限、小数位，开关只认 true / false，
    /// 下拉的值要在能选的里面（跟着别的字段走的，按这一步那个字段的值取）；合计不超过腔体的工艺超时。
    /// 字段之间不互相管。步号从 1 数，跟界面上一样；提示里的字段名按界面语言。
    /// </summary>
    private static ProcessRecipeResult? CheckSteps(IReadOnlyList<ProcessRecipeStep> steps, IReadOnlyList<ProcessRecipeField> fields,
        IReadOnlyDictionary<string, ProcessRecipeChoices> choices, double maxTotal, string? language)
    {
        if (steps.Count == 0)
        {
            return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeNoSteps);
        }

        for (int position = 0; position < steps.Count; position++)
        {
            var step = steps[position];
            string number = Text(position + 1);
            foreach (var field in fields)
            {
                string value = step.Get(field.Key).Trim();
                string fieldText = field.DisplayText(language);
                if (value.Length == 0)
                {
                    if (field.Required)
                    {
                        return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeValueRequired, number, fieldText);
                    }

                    continue;
                }

                var problem = field.CheckFormat(value);
                if (problem is not null)
                {
                    return ProcessRecipeResult.Fail(problem.Value.Code, [number, fieldText, .. problem.Value.Args]);
                }

                var source = field.Source;
                if (source is not null && !ChoicesOf(field, choices).For(source.ParentKey, step).Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeValueNotInOptions, number, fieldText, value);
                }
            }
        }

        double total = steps.Sum(ProcessRecipeData.SecondsOf);
        if (maxTotal > 0 && total > maxTotal)
        {
            return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeTotalTooLong, Number(total), Number(maxTotal));
        }

        return null;
    }

    /// <summary>
    /// 规整步骤（检查通过之后调）：只留字段表里的字段、按字段表的先后（空的也写，见 <see cref="ProcessRecipeStep"/>），
    /// 数字、开关换成统一写法，下拉换成数据源里的写法（大小写跟 sc.xml 一样）。
    /// </summary>
    private static List<ProcessRecipeStep> Normalize(IReadOnlyList<ProcessRecipeStep> steps, IReadOnlyList<ProcessRecipeField> fields,
        IReadOnlyDictionary<string, ProcessRecipeChoices> choices)
    {
        var result = new List<ProcessRecipeStep>();
        foreach (var step in steps)
        {
            var values = new List<ProcessRecipeValue>();
            foreach (var field in fields)
            {
                string value = field.Canonical(step.Get(field.Key));
                var source = field.Source;
                if (source is not null && value.Length > 0)
                {
                    value = ChoicesOf(field, choices).For(source.ParentKey, step)
                        .First(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase));
                }

                values.Add(new ProcessRecipeValue(field.Key, value));
            }

            result.Add(new ProcessRecipeStep { Values = values });
        }

        return result;
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

    private static string Number(double value)
    {
        return value.ToString(NumberFormat, CultureInfo.InvariantCulture);
    }
}
