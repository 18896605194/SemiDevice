using System.Globalization;
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

[Component(description: "工艺配方库：编号 1~N 的工艺配方，一个编号一个文件；配方头、每一步的项按下面 Head、Fields 的字段表")]
public class RecipeComponent : ComponentBase, IRecipeComponent
{
    /// <summary>
    /// 当前工艺配方库；sc.xml 里装出来即生效。冒烟与测试可以直接换成自己的实例。
    /// </summary>
    public static RecipeComponent? Current { get; set; }

    /// <summary>
    /// 步骤字段表在本节点下的分组名：一个子节点一个字段，节点名就是字段名。
    /// </summary>
    public const string FieldsNodeName = "Fields";

    /// <summary>
    /// 配方头字段表在本节点下的分组名：一个子节点一项，节点名就是字段名。
    /// </summary>
    public const string HeadNodeName = "Head";

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
    /// 错误提示里数字的写法：最多两位小数，不带多余的 0。
    /// </summary>
    private const string NumberFormat = "0.##";

    /// <summary>
    /// 名称只能用字母、数字、_ 和 -：Host、流程配方按名字选配方，文件里、日志里也不出怪字符。
    /// </summary>
    //private static readonly Regex NameRule = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    private readonly object _gate = new();

    /// <summary>
    /// 编号 → 工艺配方，按编号排好。
    /// </summary>
    private readonly SortedDictionary<int, RecipeData> _items = new();

    /// <summary>
    /// 步骤字段表，按 sc.xml 里的先后（就是步骤表从左到右）。
    /// </summary>
    private IReadOnlyList<RecipeField> _fields = [];

    /// <summary>
    /// 配方头字段表，按 sc.xml 里的先后。
    /// </summary>
    private IReadOnlyList<RecipeField> _headFields = [];

    /// <summary>
    /// 装起来的腔体：配方用到具体腔体时按名字找它核对。
    /// </summary>
    private IReadOnlyList<BaseChamberModule> _chambers = [];

    /// <summary>
    /// 腔体配的几种 Type（按 sc.xml 里的先后，不分大小写去重；没配的是空字符串）：配方头 Type 的选项。
    /// 还没 Bind（没有腔体）时只有空字符串一种，配方不分类型。
    /// </summary>
    private IReadOnlyList<string> _stationTypes = [string.Empty];

    public RecipeComponent()
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
    /// 内容变了
    /// </summary>
    public event Action<int>? Changed;

    /// <summary>
    /// 步骤字段表（装配时从 sc.xml 读的，之后不变）。
    /// </summary>
    public IReadOnlyList<RecipeField> Fields
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
    /// 配方头字段表（装配时从 sc.xml 读的，之后不变）。
    /// </summary>
    public IReadOnlyList<RecipeField> HeadFields
    {
        get
        {
            lock (_gate)
            {
                return _headFields;
            }
        }
    }

    /// <summary>
    /// 腔体配的几种 Type：配方头 Type 的选项（Bind 之后才是真的，之前只有空字符串一种）。
    /// </summary>
    public IReadOnlyList<string> StationTypes
    {
        get
        {
            lock (_gate)
            {
                return _stationTypes;
            }
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
        var headFields = ReadHeadFields(setting);
        lock (_gate)
        {
            _fields = fields;
            _headFields = headFields;
        }

        Load();
    }

    /// <summary>
    /// 读字段表：字段名不能重复；必须有 Seconds（步骤时长，小数）。
    /// </summary>
    private static List<RecipeField> ReadFields(ModuleConfig setting)
    {
        var node = setting.Children.FirstOrDefault(child => string.Equals(child.Name, FieldsNodeName, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            throw new InvalidOperationException($"sc.xml 节点 {setting.Name} 下没配 {FieldsNodeName}（工艺配方每一步有哪些字段，一个子节点一个）");
        }

        var fields = new List<RecipeField>();
        foreach (var fieldNode in node.Children)
        {
            string path = $"{setting.Name}.{FieldsNodeName}.{fieldNode.Name}";
            var field = RecipeField.FromConfig(fieldNode, path);
            if (FindField(fields, field.Key) is not null)
            {
                throw new InvalidOperationException($"sc.xml 节点 {path}：字段名 {field.Key} 重复了");
            }

            fields.Add(field);
        }

        var seconds = FindField(fields, RecipeField.SecondsKey);
        if (seconds is null || seconds.Type != ProcessRecipeFieldType.Double)
        {
            throw new InvalidOperationException(
                $"sc.xml 节点 {setting.Name}.{FieldsNodeName} 里要有 {RecipeField.SecondsKey}（步骤时长，Type 为 Double）：合计时长和工艺超时靠它");
        }

        return fields;
    }

    /// <summary>
    /// 读配方头字段表：字段名不能重复；固定的几项（<see cref="RecipeHead"/>）都要有，类型跟固定的一样。
    /// Type 那一项的选项是腔体配的几种 Type，不在 sc.xml 里写；自己加的下拉只写一套 Source。
    /// </summary>
    private static List<RecipeField> ReadHeadFields(ModuleConfig setting)
    {
        var node = setting.Children.FirstOrDefault(child => string.Equals(child.Name, HeadNodeName, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            throw new InvalidOperationException($"sc.xml 节点 {setting.Name} 下没配 {HeadNodeName}（工艺配方的配方头有哪些项，一个子节点一项）");
        }

        var fields = new List<RecipeField>();
        foreach (var fieldNode in node.Children)
        {
            string path = $"{setting.Name}.{HeadNodeName}.{fieldNode.Name}";
            bool isType = string.Equals(fieldNode.Name.Trim(), RecipeHead.TypeKey, StringComparison.OrdinalIgnoreCase);
            var field = RecipeField.FromConfig(fieldNode, path, optionsInSc: !isType);
            if (FindField(fields, field.Key) is not null)
            {
                throw new InvalidOperationException($"sc.xml 节点 {path}：字段名 {field.Key} 重复了");
            }

            fields.Add(field);
        }

        foreach (var pair in RecipeHead.Fixed)
        {
            var field = FindField(fields, pair.Key);
            if (field is null || field.Type != pair.Value)
            {
                throw new InvalidOperationException($"sc.xml 节点 {setting.Name}.{HeadNodeName} 里要有 {pair.Key}（配方头固定的一项，Type 为 {pair.Value}）");
            }
        }

        return fields;
    }

    private static RecipeField? FindField(IEnumerable<RecipeField> fields, string key)
    {
        return fields.FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 从目录把工艺配方全部读进内存（装配时调一次）。目录没有就建；文件名不是编号的、编号超出个数的、读不出来的跳过并记日志——
    /// 一个坏文件不该拖垮开机，也不该连累别的工艺配方。字段表里有、文件里没有的项按默认值补上（字段表后来加的；
    /// 配方头的超时时间没写的也补默认值，Type 没写的等 Bind 知道有哪几种腔体再补）。
    /// </summary>
    public void Load()
    {
        string folder = FolderPath();
        var fields = Fields;
        var headFields = HeadFields;
        var loaded = new SortedDictionary<int, RecipeData>();
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

                FillHeadDefaults(data, headFields);
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
    /// 记下装起来的腔体和它们配的几种 Type（装配完、模块起来之后调一次），配方用到具体腔体时按名字找腔体核对（<see cref="FindMismatch"/>）。
    /// 按这几种 Type 查字段表：步骤的下拉每种 Type 都要写了选项、默认值在每一套里；配方头自己加的下拉要写了选项、默认值在里面——配错开机就抛。
    /// 老配方没写 Type 的补成第一种（sc.xml 里排最前的腔体那种），下次保存写进文件。
    /// </summary>
    public void Bind(IEnumerable<BaseModule> modules)
    {
        var chambers = modules.OfType<BaseChamberModule>().ToList();
        var types = new List<string>();
        foreach (var chamber in chambers)
        {
            string type = chamber.Type.Trim();
            if (!types.Contains(type, StringComparer.OrdinalIgnoreCase))
            {
                types.Add(type);
            }
        }

        if (types.Count == 0)
        {
            types.Add(string.Empty);
        }

        CheckOptions(Fields, HeadFields, types);
        int filled = 0;
        lock (_gate)
        {
            _chambers = chambers;
            _stationTypes = types;
            foreach (var pair in _items.ToList())
            {
                // 库里的配方不在原地改（别处可能正拿着），换一份补好的
                if (pair.Value.Type.Trim().Length == 0 && types[0].Length > 0)
                {
                    var next = pair.Value.Clone();
                    next.Type = types[0];
                    _items[pair.Key] = next;
                    filled++;
                }
            }
        }

        LogHelper.Info(Name, $"腔体的 Type：{string.Join(", ", types.Select(TypeText))}"
            + (filled == 0 ? string.Empty : $"；{filled} 个老配方没写 Type，按 {types[0]} 补上"));
    }

    /// <summary>
    /// 按腔体的几种 Type 查字段表里的下拉（见 <see cref="Bind"/>）；写了某种 Type 的选项、却没有腔体是这种 Type 的，记一条提醒。
    /// </summary>
    private void CheckOptions(IReadOnlyList<RecipeField> fields, IReadOnlyList<RecipeField> headFields, IReadOnlyList<string> types)
    {
        foreach (var field in fields.Where(item => item.Type == ProcessRecipeFieldType.Choice))
        {
            string path = $"{Name}.{FieldsNodeName}.{field.Key}";
            foreach (string type in types)
            {
                CheckChoice(field, field.OptionsFor(type), path, $"Type {TypeText(type)} 的选项（{SourceOf(type)}）");
            }

            foreach (string written in field.OptionsByType.Keys)
            {
                if (!types.Contains(written, StringComparer.OrdinalIgnoreCase))
                {
                    LogHelper.Warn(Name, $"sc.xml 节点 {path} 写了 {SourceOf(written)}，但没有腔体的 Type 是 {TypeText(written)}，用不上");
                }
            }
        }

        foreach (var field in headFields.Where(item => item.Type == ProcessRecipeFieldType.Choice
                     && !string.Equals(item.Key, RecipeHead.TypeKey, StringComparison.OrdinalIgnoreCase)))
        {
            CheckChoice(field, field.OptionsFor(string.Empty), $"{Name}.{HeadNodeName}.{field.Key}", $"选项（配方头只写一套 {SourceOf(string.Empty)}）");
        }
    }

    /// <summary>
    /// 一套下拉选项不能是空的，默认值（写了的话）要在里面。
    /// </summary>
    private static void CheckChoice(RecipeField field, IReadOnlyList<string> options, string path, string what)
    {
        if (options.Count == 0)
        {
            throw new InvalidOperationException($"sc.xml 节点 {path} 是下拉，没写{what}");
        }

        if (field.Default.Length > 0 && !options.Contains(field.Default, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"sc.xml 节点 {path} 的 Default=\"{field.Default}\" 不在{what}里");
        }
    }

    /// <summary>
    /// 日志、报错里 Type 的写法：没配的写成"（空）"。
    /// </summary>
    private static string TypeText(string type)
    {
        return type.Length == 0 ? "（空）" : type;
    }

    /// <summary>
    /// 这种 Type 的选项在 sc.xml 里写在哪个参数：没配 Type 的是 Source，配了的是 Source.类型。
    /// </summary>
    private static string SourceOf(string type)
    {
        return type.Length == 0 ? RecipeField.SourceName : $"{RecipeField.SourceName}.{type}";
    }

    /// <summary>
    /// 全部工艺配方（副本），按编号从小到大。
    /// </summary>
    public IReadOnlyList<RecipeData> List()
    {
        lock (_gate)
        {
            return _items.Values.Select(item => item.Clone()).ToList();
        }
    }

    /// <summary>
    /// 按编号取一个（副本）。
    /// </summary>
    public HandleResult Get(int index)
    {
        lock (_gate)
        {
            return CheckExists(index) ?? HandleResult.Success(_items[index].Clone());
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
    public RecipeData? Find(string name)
    {
        lock (_gate)
        {
            return FindByName(name.Trim())?.Clone();
        }
    }

    /// <summary>
    /// 这个配方用在这个腔体上对不对得上（流程配方保存时按勾的腔体查）：由腔体照名字找配方里选的摆臂、药液
    /// （<see cref="BaseChamberModule.FindRecipeMismatch"/>，几个腔体装的不一样时才会对不上）。
    /// 对得上、腔体不认识（不是腔体，或没绑上）、配方不在库里（那是另一个错，由 <see cref="Contains"/> 那一步报）都返回 null。
    /// </summary>
    public (string Field, string Value)? FindMismatch(string recipeName, string chamber)
    {
        string wanted = recipeName.Trim();
        string chamberName = chamber.Trim();
        RecipeData? recipe;
        BaseChamberModule? found;
        lock (_gate)
        {
            recipe = FindByName(wanted);
            found = _chambers.FirstOrDefault(item => string.Equals(item.Name, chamberName, StringComparison.OrdinalIgnoreCase));
        }

        // 库里的配方存的时候整份换掉、不在原地改，锁外读也不会读到一半
        return recipe is null || found is null ? null : found.FindRecipeMismatch(recipe);
    }

    /// <summary>
    /// 新建：空编号上建一个，名称要合规、不重名。配方头填默认值（Type 取腔体的第一种），步骤先给一步（各字段的默认值），人再改。
    /// </summary>
    public HandleResult Create(int index, string name, string operatorName)
    {
        name = name.Trim();
        HandleResult result;
        lock (_gate)
        {
            var now = DateTime.Now;
            var data = new RecipeData
            {
                Index = index,
                Name = name,
                Type = _stationTypes[0],
                CreatedBy = operatorName,
                CreatedAt = now,
                ModifiedBy = operatorName,
                ModifiedAt = now,
                Revision = 1,
                Steps = [NewStep(_fields)],
            };
            FillHeadDefaults(data, _headFields);
            result = CheckIndex(index) ?? CheckFree(index) ?? CheckName(name, index) ?? Store(data);
        }

        Report(result, index, $"新建工艺配方 {index} 号 {name}（操作人 {operatorName}）", (name, ChangeKind.Created));
        return result;
    }

    /// <summary>
    /// 重命名：名称要合规、不跟别的编号重；版本加 1，记修改人、修改时间。
    /// </summary>
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
        Report(result, index, $"工艺配方 {index} 号改名为 {name}（操作人 {operatorName}）", (oldName, ChangeKind.Deleted), (name, ChangeKind.Created));
        return result;
    }

    /// <summary>
    /// 删除：文件删掉、编号空出来。
    /// </summary>
    public HandleResult Delete(int index, string operatorName)
    {
        HandleResult result;
        string name = string.Empty;
        lock (_gate)
        {
            result = CheckExists(index) ?? Remove(index, out name);
        }

        Report(result, index, $"删除工艺配方 {index} 号 {name}（操作人 {operatorName}）", (name, ChangeKind.Deleted));
        return result;
    }

    /// <summary>
    /// 保存配方头里能填的项（Type、超时时间、说明、字段表里自己加的项）和步骤：打开时读到的版本要对得上，配方头、步骤要按字段表检查通过；
    /// 存完版本加 1，记修改人、修改时间。存之前规整一下（见 <see cref="ApplyContent"/>）。content 里的编号、名称、建 / 改的人和时间、版本不管。
    /// </summary>
    public HandleResult Save(int index, int revision, RecipeData content, string operatorName)
    {
        string? language = SystemComponent.Current?.Language;
        HandleResult result;
        lock (_gate)
        {
            result = CheckExists(index)
                ?? CheckRevision(index, revision)
                ?? CheckContent(content, language)
                ?? Store(Touch(_items[index], operatorName, next => ApplyContent(next, content)));
        }

        Report(result, index, $"保存工艺配方 {index} 号（操作人 {operatorName}）", ((result.Result as RecipeData)?.Name ?? string.Empty, ChangeKind.Edited));
        return result;
    }

    /// <summary>
    /// 成了记一条日志、发变更事件（锁外），接着 EAP 的话把配方怎么变的报给 Host（放进 EAP 的派发组件）；
    /// 没成不记（原因回给界面，写文件失败的在写的地方已经记过）。
    /// </summary>
    private void Report(HandleResult result, int index, string message, params (string Name, ChangeKind Change)[] changes)
    {
        if (!result.IsSuccess)
        {
            return;
        }

        var saved = result.Result as RecipeData;
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
    public IReadOnlyList<string> RecipeNames
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
    public string? GetRecipe(string name)
    {
        var found = Find(name);
        return found is null ? null : JsonHelper.Serialize(found);
    }

    /// <summary>JSON 看样子是不是工艺配方：每一步都带 values（字段名 → 值）。</summary>
    public bool AcceptsRecipe(string json)
    {
        return JsonSteps.AllHave(json, "values");
    }

    /// <summary>
    /// Host 下的工艺配方：库里没有就在第一个空编号上新建（版本 1），有就把配方头能填的项和步骤整个换掉（版本加 1）；
    /// 名字、配方头、每一步的检查（按字段表、合计时长）跟本地新建、保存一样，JSON 里的编号、版本、建 / 改的人和时间不管。
    /// JSON 里没带超时时间、自己加的项的按默认值补（跟读老配方一样）；Type 不补，没带就不收。
    /// </summary>
    public HandleResult ImportRecipe(string name, string json, string operatorName)
    {
        name = name.Trim();
        var body = ParseBody(json);
        if (body is null)
        {
            return HandleResult.Fail(ErrorCodes.ProcessRecipeBodyInvalid, name);
        }

        string? language = SystemComponent.Current?.Language;
        HandleResult result;
        int index;
        string reported;
        bool created;
        lock (_gate)
        {
            FillHeadDefaults(body, _headFields);
            var existing = FindByName(name);
            created = existing is null;
            if (existing is null)
            {
                index = FirstFreeIndex();
                reported = name;
                var now = DateTime.Now;
                var data = new RecipeData
                {
                    Index = index,
                    Name = name,
                    CreatedBy = operatorName,
                    CreatedAt = now,
                    ModifiedBy = operatorName,
                    ModifiedAt = now,
                    Revision = 1,
                };
                result = (index == 0 ? HandleResult.Fail(ErrorCodes.ProcessRecipeFull, Text(Capacity)) : null)
                    ?? CheckName(name, index)
                    ?? CheckContent(body, language);
                if (result is null)
                {
                    ApplyContent(data, body);
                    result = Store(data);
                }
            }
            else
            {
                index = existing.Index;
                reported = existing.Name;
                result = CheckContent(body, language) ?? Store(Touch(existing, operatorName, next => ApplyContent(next, body)));
            }
        }

        Report(result, index, $"{(created ? "远程新建" : "远程覆盖")}工艺配方 {index} 号 {reported}（操作人 {operatorName}）",
            (reported, created ? ChangeKind.Created : ChangeKind.Edited));
        return result;
    }

    /// <summary>按名字删一个工艺配方（Host 删）。</summary>
    public HandleResult DeleteRecipe(string name, string operatorName)
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
                return HandleResult.Fail(ErrorCodes.ProcessRecipeNameNotFound, name);
            }

            index = existing.Index;
            result = Remove(index, out removed);
        }

        Report(result, index, $"远程删除工艺配方 {index} 号 {removed}（操作人 {operatorName}）", (removed, ChangeKind.Deleted));
        return result;
    }

    /// <summary>
    /// Host 给的 JSON 转成工艺配方（只用说明和步骤）；读不出来返回 null。空着的说明、步骤、字段名、值补成空的，后面按字段表照常查。
    /// </summary>
    private static RecipeData? ParseBody(string json)
    {
        RecipeData? body;
        try
        {
            body = JsonHelper.Deserialize<RecipeData>(json);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            return null;
        }

        if (body is null)
        {
            return null;
        }

        body.Type = body.Type ?? string.Empty;
        body.Timeout = body.Timeout ?? string.Empty;
        body.Description = body.Description ?? string.Empty;
        body.Head = CleanValues(body.Head);
        body.Steps = (body.Steps ?? []).Where(step => step is not null).Select(CleanValues).ToList();
        return body;
    }

    /// <summary>
    /// JSON 里空着的一组值、字段名、值补成空的（后面按字段表照常查）。
    /// </summary>
    private static RecipeStep CleanValues(RecipeStep? values)
    {
        return new RecipeStep
        {
            Values = (values?.Values ?? [])
                .Where(value => value is not null)
                .Select(value => new RecipeValue(value.Name ?? string.Empty, value.Value ?? string.Empty))
                .ToList(),
        };
    }

    #endregion

    /// <summary>按名字找（不分大小写）；必须在 _gate 里调。</summary>
    private RecipeData? FindByName(string name)
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
    private static RecipeData Touch(RecipeData current, string operatorName, Action<RecipeData> change)
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
    private HandleResult Store(RecipeData data)
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
            return HandleResult.Fail(ErrorCodes.ProcessRecipeSaveFailed, Text(data.Index), exception.Message);
        }

        _items[data.Index] = data;
        return HandleResult.Success(data.Clone());
    }

    /// <summary>
    /// 删文件，成了才从内存里拿掉。必须在 _gate 里调。
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
            LogHelper.Error(Name, $"{index} 号工艺配方文件删不掉，库里的没动：{exception.Message}");
            return HandleResult.Fail(ErrorCodes.ProcessRecipeSaveFailed, Text(index), exception.Message);
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
    private RecipeData? ReadFile(string file)
    {
        if (!int.TryParse(System.IO.Path.GetFileNameWithoutExtension(file), NumberStyles.None, CultureInfo.InvariantCulture, out int index)
            || index < 1 || index > Capacity)
        {
            LogHelper.Warn(Name, $"跳过 {file}：文件名不是 1~{Capacity} 的编号");
            return null;
        }

        try
        {
            var data = XmlHelper.Deserialize<RecipeData>(file);
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
    private static void FillDefaults(RecipeStep step, IReadOnlyList<RecipeField> fields)
    {
        foreach (var field in fields)
        {
            if (!step.Has(field.Key))
            {
                step.Values.Add(new RecipeValue(field.Key, field.Default));
            }
        }
    }

    /// <summary>
    /// 配方头没写的项按默认值补：超时时间空着的补默认值（老配方里还没有这一项），自己加的项没有的补默认值（字段表后来加的）。
    /// Type 不在这儿补：要知道有哪几种腔体，Bind 时补。说明空着就是空着。
    /// </summary>
    private static void FillHeadDefaults(RecipeData data, IReadOnlyList<RecipeField> headFields)
    {
        var timeout = FindField(headFields, RecipeHead.TimeoutKey);
        if (timeout is not null && data.Timeout.Trim().Length == 0)
        {
            data.Timeout = timeout.Default;
        }

        FillDefaults(data.Head, headFields.Where(field => RecipeHead.IsCustom(field.Key)).ToList());
    }

    /// <summary>
    /// 新加的一步：每个字段填默认值。
    /// </summary>
    private static RecipeStep NewStep(IReadOnlyList<RecipeField> fields)
    {
        return new RecipeStep { Values = fields.Select(field => new RecipeValue(field.Key, field.Default)).ToList() };
    }

    private HandleResult? CheckIndex(int index)
    {
        return index < 1 || index > Capacity
            ? HandleResult.Fail(ErrorCodes.ProcessRecipeIndexOutOfRange, Text(index), Text(Capacity))
            : null;
    }

    private HandleResult? CheckExists(int index)
    {
        return CheckIndex(index) ?? (_items.ContainsKey(index) ? null : HandleResult.Fail(ErrorCodes.ProcessRecipeNotFound, Text(index)));
    }

    private HandleResult? CheckFree(int index)
    {
        return _items.TryGetValue(index, out var existing)
            ? HandleResult.Fail(ErrorCodes.ProcessRecipeIndexOccupied, Text(index), existing.Name)
            : null;
    }

    private HandleResult? CheckRevision(int index, int revision)
    {
        return _items[index].Revision == revision ? null : HandleResult.Fail(ErrorCodes.ProcessRecipeRevisionMismatch, Text(index));
    }

    /// <summary>
    /// 名称：必填、不超长、只用字母数字 _ -、不跟别的编号重（不分大小写）。
    /// </summary>
    private HandleResult? CheckName(string name, int selfIndex)
    {
        if (name.Length == 0)
        {
            return HandleResult.Fail(ErrorCodes.ProcessRecipeNameRequired);
        }

        if (name.Length > NameMaxLength)
        {
            return HandleResult.Fail(ErrorCodes.ProcessRecipeNameTooLong, Text(NameMaxLength));
        }

        foreach (var pair in _items)
        {
            if (pair.Key != selfIndex && string.Equals(pair.Value.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return HandleResult.Fail(ErrorCodes.ProcessRecipeNameDuplicate, name, Text(pair.Key));
            }
        }

        return null;
    }

    /// <summary>
    /// 查一份配方的内容（必须在 _gate 里调）：先查配方头（步骤下拉的选项按配方头的 Type 取），再查步骤和合计时长。
    /// </summary>
    private HandleResult? CheckContent(RecipeData content, string? language)
    {
        return CheckHead(content, language) ?? CheckSteps(content.Steps, _fields, content.Type.Trim(), content.TimeoutSeconds, language);
    }

    /// <summary>
    /// 配方头里能填的项（程序算的那几项不查）：Type 要是腔体配的几种之一（没配 Type 的腔体也算一种，那种就是空着）；
    /// 超时时间、说明、自己加的项按字段表查，规则跟步骤一样，提示用配方头那一组错误码、字段名按界面语言。必须在 _gate 里调。
    /// </summary>
    private HandleResult? CheckHead(RecipeData content, string? language)
    {
        foreach (var field in _headFields)
        {
            if (RecipeHead.IsAuto(field.Key))
            {
                continue;
            }

            bool isType = string.Equals(field.Key, RecipeHead.TypeKey, StringComparison.OrdinalIgnoreCase);
            var options = isType ? _stationTypes : field.OptionsFor(string.Empty);
            string value = HeadValue(content, field.Key).Trim();
            string fieldText = field.DisplayText(language);
            if (value.Length == 0)
            {
                bool required = isType ? !options.Contains(string.Empty) : field.Required;
                if (required)
                {
                    return HandleResult.Fail(ErrorCodes.ProcessRecipeHeadValueRequired, fieldText);
                }

                continue;
            }

            var problem = field.CheckFormat(value, options);
            if (problem is not null)
            {
                return HandleResult.Fail(HeadCode(problem.Value.Code), [fieldText, .. problem.Value.Args]);
            }
        }

        return null;
    }

    /// <summary>
    /// 配方头里一项的值：固定的几项在各自的属性上，自己加的在 Head 里。
    /// </summary>
    private static string HeadValue(RecipeData content, string key)
    {
        if (string.Equals(key, RecipeHead.TypeKey, StringComparison.OrdinalIgnoreCase))
        {
            return content.Type;
        }

        if (string.Equals(key, RecipeHead.TimeoutKey, StringComparison.OrdinalIgnoreCase))
        {
            return content.Timeout;
        }

        if (string.Equals(key, RecipeHead.DescriptionKey, StringComparison.OrdinalIgnoreCase))
        {
            return content.Description;
        }

        return content.Head.Get(key);
    }

    /// <summary>
    /// 字段查出来的错误码（步骤那一组）换成配方头那一组。
    /// </summary>
    private static string HeadCode(string stepCode)
    {
        switch (stepCode)
        {
            case ErrorCodes.ProcessRecipeValueNotInteger:
                return ErrorCodes.ProcessRecipeHeadValueNotInteger;
            case ErrorCodes.ProcessRecipeValueNotNumber:
                return ErrorCodes.ProcessRecipeHeadValueNotNumber;
            case ErrorCodes.ProcessRecipeValueBelowMin:
                return ErrorCodes.ProcessRecipeHeadValueBelowMin;
            case ErrorCodes.ProcessRecipeValueAboveMax:
                return ErrorCodes.ProcessRecipeHeadValueAboveMax;
            case ErrorCodes.ProcessRecipeValueTooPrecise:
                return ErrorCodes.ProcessRecipeHeadValueTooPrecise;
            default:
                return ErrorCodes.ProcessRecipeHeadValueNotInOptions;
        }
    }

    /// <summary>
    /// 把查过的内容规整后放进 next（必须在 _gate 里调）：Type 换成腔体配的写法；超时时间、自己加的项换成统一写法，
    /// 自己加的项只留字段表里有的、按字段表的先后；说明去掉首尾空白；步骤见 <see cref="Normalize"/>（下拉按配方的 Type 取选项）。
    /// </summary>
    private void ApplyContent(RecipeData next, RecipeData content)
    {
        string type = content.Type.Trim();
        next.Type = _stationTypes.FirstOrDefault(item => string.Equals(item, type, StringComparison.OrdinalIgnoreCase)) ?? type;
        var timeout = FindField(_headFields, RecipeHead.TimeoutKey)!;
        next.Timeout = timeout.Canonical(content.Timeout, []);
        next.Description = content.Description.Trim();
        next.Head = new RecipeStep
        {
            Values = _headFields
                .Where(field => RecipeHead.IsCustom(field.Key))
                .Select(field => new RecipeValue(field.Key, field.Canonical(content.Head.Get(field.Key), field.OptionsFor(string.Empty))))
                .ToList(),
        };
        next.Steps = Normalize(content.Steps, _fields, next.Type);
    }

    /// <summary>
    /// 步骤：至少一步；每一步每个字段按字段表查——必填的不能空，整数、小数的写法、上下限、小数位，开关只认 true / false，
    /// 下拉的值要在配方这种 Type 的选项里；合计不超过配方头的超时时间（没填不限）。
    /// 字段之间不互相管。步号从 1 数，跟界面上一样；提示里的字段名按界面语言。
    /// </summary>
    private static HandleResult? CheckSteps(IReadOnlyList<RecipeStep> steps, IReadOnlyList<RecipeField> fields,
        string stationType, double timeoutSeconds, string? language)
    {
        if (steps.Count == 0)
        {
            return HandleResult.Fail(ErrorCodes.ProcessRecipeNoSteps);
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
                        return HandleResult.Fail(ErrorCodes.ProcessRecipeValueRequired, number, fieldText);
                    }

                    continue;
                }

                var problem = field.CheckFormat(value, field.OptionsFor(stationType));
                if (problem is not null)
                {
                    return HandleResult.Fail(problem.Value.Code, [number, fieldText, .. problem.Value.Args]);
                }
            }
        }

        double total = steps.Sum(RecipeData.SecondsOf);
        if (timeoutSeconds > 0 && total > timeoutSeconds)
        {
            return HandleResult.Fail(ErrorCodes.ProcessRecipeTotalTooLong, Number(total), Number(timeoutSeconds));
        }

        return null;
    }

    /// <summary>
    /// 规整步骤（检查通过之后调）：只留字段表里的字段、按字段表的先后（空的也写，见 <see cref="RecipeStep"/>），
    /// 数字、开关换成统一写法，下拉换成这种 Type 选项里的写法（大小写跟 sc.xml 一样）。
    /// </summary>
    private static List<RecipeStep> Normalize(IReadOnlyList<RecipeStep> steps, IReadOnlyList<RecipeField> fields, string stationType)
    {
        var result = new List<RecipeStep>();
        foreach (var step in steps)
        {
            var values = new List<RecipeValue>();
            foreach (var field in fields)
            {
                values.Add(new RecipeValue(field.Key, field.Canonical(step.Get(field.Key), field.OptionsFor(stationType))));
            }

            result.Add(new RecipeStep { Values = values });
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
