using System.Globalization;
using System.Text.RegularExpressions;
using xyz.Common.Log;
using xyz.Components;
using xyz.Components.Attributes;
using xyz.Components.Components;
using xyz.Configs.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Tools;

namespace xyz.Modules;

/// <summary>
/// 工艺配方库（配方 → 工艺配方页）：编号 1~Capacity，一个编号一个文件（Folder 下 001.xml、002.xml……）。
/// 工艺配方说的是片进了腔体以后怎么做：一步一步转多快、哪条摆臂喷什么药液、喷多少、停在一个位置喷（Time）还是来回扫（Scan）。
/// 能选的摆臂和药液不写死：模块全起来后 Bind 一次，按腔体下装的摆臂轴和挂在它下面的喷嘴（喷嘴的 Chemical）生成；
/// 合计时长的上限跟腔体的工艺超时（EC，现查）走。流程配方的工艺步骤、腔体起工艺都按名字引用这里的配方。
/// 只有 gRPC 线程调它（设备扫描线程不碰），所以读写文件放在锁里也卡不到设备，还省得两次保存交叉写坏文件。
/// </summary>
[Component(description: "工艺配方库：编号 1~N 的工艺配方，一个编号一个文件")]
public class ProcessRecipeComponent : ComponentBase
{
    /// <summary>
    /// 当前工艺配方库；sc.xml 里装出来即生效。冒烟与测试可以直接换成自己的实例。
    /// </summary>
    public static ProcessRecipeComponent? Current { get; set; }

    /// <summary>
    /// 时间、流量、扫描速度的下限：都按 0.1 记，0 没有意义（喷 0 秒、0 流量、扫描不动）。
    /// </summary>
    public const double MinPositiveValue = 0.1;

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
    /// 新加的一步（新建工艺配方时的第一步、页面上"添加"的那一步）：不出液、转着，数是个常见的起点，人再改。
    /// </summary>
    public const double NewStepSeconds = 10;

    public const int NewStepRpm = 500;

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

    private readonly object _gate = new();

    /// <summary>
    /// 编号 → 工艺配方，按编号排好。
    /// </summary>
    private readonly SortedDictionary<int, ProcessRecipeData> _items = new();

    private IReadOnlyList<ProcessRecipeArm> _arms = [];

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

    [SCEditor("3600", "ProcessRecipe", "一步最长多少秒")]
    public double MaxStepSeconds { get; set; } = 3600;

    [SCEditor("3000", "ProcessRecipe", "转速上限 rpm（旋转电机能到的）")]
    public int MaxRpm { get; set; } = 3000;

    [SCEditor("3", "ProcessRecipe", "流量上限 L/min（喷嘴流量设定的量程）")]
    public double MaxFlow { get; set; } = 3;

    [SCEditor("100", "ProcessRecipe", "摆臂扫描速度上限 mm/s")]
    public double MaxScanSpeed { get; set; } = 100;

    #endregion

    /// <summary>
    /// 内容变了（新建、改名、删除、保存），参数是编号。在锁外发。
    /// </summary>
    public event Action<int>? Changed;

    /// <summary>
    /// 能选的摆臂和它上面的药液（Bind 之后才有），按 sc.xml 里的先后。
    /// </summary>
    public IReadOnlyList<ProcessRecipeArm> Arms
    {
        get
        {
            lock (_gate)
            {
                return _arms;
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
    /// 装配读完 SC：先查参数（配错了开机就报出来），再从目录把工艺配方读进来。
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
            throw new InvalidOperationException($"sc.xml 节点 {Name} 没配 Folder（工艺配方文件放哪）");
        }

        if (!(MaxStepSeconds >= MinPositiveValue) || MaxRpm < 0 || !(MaxFlow >= MinPositiveValue) || !(MaxScanSpeed >= MinPositiveValue))
        {
            throw new InvalidOperationException(
                $"sc.xml 节点 {Name} 的上限不对：MaxStepSeconds、MaxFlow、MaxScanSpeed 至少 {Number(MinPositiveValue)}，MaxRpm 不能小于 0" +
                $"（现在 {Number(MaxStepSeconds)} / {Number(MaxFlow)} / {Number(MaxScanSpeed)} / {MaxRpm}）");
        }

        Load();
    }

    /// <summary>
    /// 从目录把工艺配方全部读进内存（装配时调一次）。目录没有就建；文件名不是编号的、编号超出个数的、读不出来的跳过并记日志——
    /// 一个坏文件不该拖垮开机，也不该连累别的工艺配方。
    /// </summary>
    public void Load()
    {
        string folder = FolderPath();
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
    /// 生成能选的摆臂和药液（装配完、模块起来之后调一次）：按腔体下装的摆臂轴、和挂在摆臂下面的喷嘴的 Chemical，
    /// 几个腔体同名的摆臂合成一条（药液取并集，按 sc.xml 里的先后）；一个喷嘴都没有的摆臂选了也喷不了，不列。
    /// 腔体记下来，合计时长的上限按它们的工艺超时现查。
    /// </summary>
    public void Bind(IEnumerable<BaseModule> modules)
    {
        var chambers = modules.OfType<BaseChamberModule>().ToList();
        var order = new List<string>();
        var chemicals = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var chamber in chambers)
        {
            foreach (var arm in chamber.FindChildren<ArmAxisComponent>())
            {
                if (!chemicals.TryGetValue(arm.Name, out var list))
                {
                    list = [];
                    chemicals[arm.Name] = list;
                    order.Add(arm.Name);
                }

                foreach (var nozzle in arm.FindChildren<NozzleComponent>())
                {
                    string chemical = nozzle.Chemical.Trim();
                    if (chemical.Length > 0 && !list.Contains(chemical, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Add(chemical);
                    }
                }
            }
        }

        var arms = order.Where(name => chemicals[name].Count > 0).Select(name => new ProcessRecipeArm(name, chemicals[name])).ToList();
        lock (_gate)
        {
            _arms = arms;
            _chambers = chambers;
        }

        LogHelper.Info(Name, arms.Count == 0
            ? "没有能选的摆臂（腔体下没装摆臂轴，或摆臂下没有喷嘴），工艺配方只能编不出液的步骤"
            : $"能选的摆臂 {arms.Count} 条：{string.Join("；", arms.Select(arm => $"{arm.Name}（{string.Join(", ", arm.Chemicals)}）"))}");
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
    /// 新建：空编号上建一个，名称要合规、不重名。内容先给一步（不出液、转着），人再改。
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
                Steps = [new ProcessRecipeStep { Seconds = NewStepSeconds, Rpm = NewStepRpm }],
            });
        }

        Report(result, index, $"新建工艺配方 {index} 号 {name}（操作人 {operatorName}）");
        return result;
    }

    /// <summary>
    /// 重命名：名称要合规、不跟别的编号重；版本加 1，记修改人、修改时间。
    /// </summary>
    public ProcessRecipeResult Rename(int index, string name, string operatorName)
    {
        name = name.Trim();
        ProcessRecipeResult result;
        lock (_gate)
        {
            result = CheckExists(index) ?? CheckName(name, index) ?? Store(Touch(_items[index], operatorName, next => next.Name = name));
        }

        Report(result, index, $"工艺配方 {index} 号改名为 {name}（操作人 {operatorName}）");
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

        Report(result, index, $"删除工艺配方 {index} 号 {name}（操作人 {operatorName}）");
        return result;
    }

    /// <summary>
    /// 保存说明和步骤：打开时读到的版本要对得上，步骤要检查通过；存完版本加 1，记修改人、修改时间。
    /// 存之前把步骤规整一下：摆臂名、药液按 sc 里的写法，用不上的字段清掉（不出液的清药液、流量、位置；Time 清 Scan 的另一头和速度）。
    /// </summary>
    public ProcessRecipeResult Save(int index, int revision, string description, IReadOnlyList<ProcessRecipeStep> steps, string operatorName)
    {
        double maxTotal = MaxTotalSeconds;
        ProcessRecipeResult result;
        lock (_gate)
        {
            var arms = _arms;
            result = CheckExists(index)
                ?? CheckRevision(index, revision)
                ?? CheckSteps(steps, arms, maxTotal)
                ?? Store(Touch(_items[index], operatorName, next =>
                {
                    next.Description = description.Trim();
                    next.Steps = Normalize(steps, arms);
                }));
        }

        Report(result, index, $"保存工艺配方 {index} 号（操作人 {operatorName}）");
        return result;
    }

    /// <summary>
    /// 成了记一条日志、发变更事件（锁外）；没成不记（原因回给界面，写文件失败的在写的地方已经记过）。
    /// </summary>
    private void Report(ProcessRecipeResult result, int index, string message)
    {
        if (!result.IsOk)
        {
            return;
        }

        var saved = result.Recipe;
        LogHelper.Info(Name, saved is null ? message : $"{message}，版本 {saved.Revision}");
        Changed?.Invoke(index);
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
    /// 步骤：至少一步；每步时间、转速在范围里；选了摆臂的——摆臂还在、选了药液、药液在这条摆臂上、流量和位置在范围里，
    /// Scan 的两头不一样、速度在范围里；合计不超过腔体的工艺超时。步号从 1 数，跟界面上一样。
    /// </summary>
    private ProcessRecipeResult? CheckSteps(IReadOnlyList<ProcessRecipeStep> steps, IReadOnlyList<ProcessRecipeArm> arms, double maxTotal)
    {
        if (steps.Count == 0)
        {
            return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeNoSteps);
        }

        for (int position = 0; position < steps.Count; position++)
        {
            var problem = CheckStep(steps[position], Text(position + 1), arms);
            if (problem is not null)
            {
                return problem;
            }
        }

        double total = steps.Sum(step => step.Seconds);
        if (maxTotal > 0 && total > maxTotal)
        {
            return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeTotalTooLong, Number(total), Number(maxTotal));
        }

        return null;
    }

    private ProcessRecipeResult? CheckStep(ProcessRecipeStep step, string number, IReadOnlyList<ProcessRecipeArm> arms)
    {
        if (!InRange(step.Seconds, MinPositiveValue, MaxStepSeconds))
        {
            return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeTimeOutOfRange, number, Number(MinPositiveValue), Number(MaxStepSeconds));
        }

        if (step.Rpm < 0 || step.Rpm > MaxRpm)
        {
            return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeRpmOutOfRange, number, Text(MaxRpm));
        }

        string armName = step.Arm.Trim();
        if (armName.Length == 0)
        {
            return null;
        }

        var arm = FindArm(arms, armName);
        if (arm is null)
        {
            return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeArmNotFound, number, armName);
        }

        string chemical = step.Chemical.Trim();
        if (chemical.Length == 0)
        {
            return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeChemicalRequired, number, arm.Name);
        }

        if (!arm.Chemicals.Contains(chemical, StringComparer.OrdinalIgnoreCase))
        {
            return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeChemicalNotOnArm, number, chemical, arm.Name);
        }

        if (!InRange(step.Flow, MinPositiveValue, MaxFlow))
        {
            return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeFlowOutOfRange, number, Number(MinPositiveValue), Number(MaxFlow));
        }

        if (!OnWafer(step.Position) || (step.Mode == ProcessArmMode.Scan && !OnWafer(step.ScanTo)))
        {
            return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipePositionOutOfRange, number,
                Number(ArmAxisComponent.WaferEdgePosition), Number(ArmAxisComponent.WaferCenterPosition));
        }

        if (step.Mode != ProcessArmMode.Scan)
        {
            return null;
        }

        if (step.ScanTo == step.Position)
        {
            return ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeScanSamePosition, number);
        }

        return InRange(step.ScanSpeed, MinPositiveValue, MaxScanSpeed)
            ? null
            : ProcessRecipeResult.Fail(ErrorCodes.ProcessRecipeScanSpeedOutOfRange, number, Number(MinPositiveValue), Number(MaxScanSpeed));
    }

    /// <summary>
    /// 规整步骤（检查通过之后调）：摆臂名、药液换成 sc 里的写法；不出液的清掉药液、流量、方式、位置，Time 清掉 Scan 的另一头和速度。
    /// </summary>
    private static List<ProcessRecipeStep> Normalize(IReadOnlyList<ProcessRecipeStep> steps, IReadOnlyList<ProcessRecipeArm> arms)
    {
        var result = new List<ProcessRecipeStep>();
        foreach (var step in steps)
        {
            var next = new ProcessRecipeStep { Seconds = step.Seconds, Rpm = step.Rpm };
            var arm = step.Arm.Trim().Length == 0 ? null : FindArm(arms, step.Arm.Trim());
            if (arm is not null)
            {
                next.Arm = arm.Name;
                next.Chemical = arm.Chemicals.First(chemical => string.Equals(chemical, step.Chemical.Trim(), StringComparison.OrdinalIgnoreCase));
                next.Flow = step.Flow;
                next.Mode = step.Mode;
                next.Position = step.Position;
                if (step.Mode == ProcessArmMode.Scan)
                {
                    next.ScanTo = step.ScanTo;
                    next.ScanSpeed = step.ScanSpeed;
                }
            }

            result.Add(next);
        }

        return result;
    }

    private static ProcessRecipeArm? FindArm(IReadOnlyList<ProcessRecipeArm> arms, string name)
    {
        return arms.FirstOrDefault(arm => string.Equals(arm.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 在 [min, max] 里（NaN、无穷都不算）。
    /// </summary>
    private static bool InRange(double value, double min, double max)
    {
        return value >= min && value <= max;
    }

    /// <summary>
    /// 在晶圆上：晶圆坐标 0（边缘）~ 150（中心）。
    /// </summary>
    private static bool OnWafer(double position)
    {
        return InRange(position, ArmAxisComponent.WaferEdgePosition, ArmAxisComponent.WaferCenterPosition);
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
