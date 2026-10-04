using xyz.Components;
using xyz.Components.Components;
using xyz.Configs.Models;
using xyz.Modules;
using xyz.Service;
using xyz.Service.Recipes;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Tools;

// 工艺配方冒烟：sc.xml 节点和字段表、字段表配错开机就报、下拉按数据源取选项（直接写的、从腔体部件取的、跟着别的字段走的，
// 几个腔体装的不一样时合起来）、新建 / 改名 / 保存 / 删除和按字段表的每一条检查、规整（数字、开关、下拉的写法，只留字段表里的字段）、
// 文件读写（字段都写进文件、老配方缺的字段按默认值补、坏文件跳过）、版本冲突、变更事件、配方用到具体腔体时对不对得上、
// gRPC 服务的列表 / 字段表 / 错误码，腔体起工艺时配方要在库里、选的这个腔体要有。不连设备，文件写在临时目录，跑完删掉。
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("FAIL: " + message);
    }

    checks++;
}

void Fails(ProcessRecipeResult result, string code, string[] args, string message)
{
    Check(!result.IsOk && result.Code == code && result.Args.SequenceEqual(args),
        $"{message}，实际 {result.Code} [{string.Join(",", result.Args)}]");
}

// 一步：按 sc.xml 字段表的字段名给值，不给的就是空的
ProcessRecipeStep Step(string seconds, string rpm, string arm = "", string chemical = "", string flow = "", string mode = "",
    string position = "", string scanTo = "", string scanSpeed = "")
{
    var values = new List<ProcessRecipeValue>
    {
        new("Seconds", seconds), new("Rpm", rpm), new("Arm", arm), new("Chemical", chemical), new("Flow", flow),
        new("Mode", mode), new("Position", position), new("ScanTo", scanTo), new("ScanSpeed", scanSpeed),
    };
    return new ProcessRecipeStep { Values = values };
}

// 探针腔体：下面按给的装摆臂轴，摆臂下面按药液装喷嘴（跟 sc.xml 里的层级一样）
ProbeChamber Chamber(string name, params (string Arm, string[] Chemicals)[] arms)
{
    var chamber = new ProbeChamber(name);
    foreach (var (armName, chemicals) in arms)
    {
        var arm = new ArmAxisComponent();
        Probe.Name(arm, armName);
        chamber.AddChild(arm);
        foreach (string chemical in chemicals)
        {
            var nozzle = new NozzleComponent { Chemical = chemical };
            Probe.Name(nozzle, "Nozzle_" + chemical);
            arm.AddChild(nozzle);
        }
    }

    return chamber;
}

// 自己拼的工艺配方节点（查字段表配错的情况、开关和文本字段用）
string folder = Path.Combine(Path.GetTempPath(), "xyz-process-recipe-smoke-" + Guid.NewGuid().ToString("N"));
string otherFolder = folder + "-other";
ModuleConfig FieldNode(string key, params (string Name, string Value)[] values)
{
    return new ModuleConfig { Name = key, Values = values.Select(value => new ValueConfig { Name = value.Name, Value = value.Value }).ToList() };
}

ModuleConfig SecondsNode()
{
    return FieldNode("Seconds", ("Text", "时间"), ("Type", "Double"), ("Min", "0.1"), ("Required", "true"));
}

ModuleConfig LibraryNode(params ModuleConfig[] fields)
{
    return new ModuleConfig
    {
        Name = "ProcessRecipe",
        Type = typeof(ProcessRecipeComponent).FullName,
        Values = [new ValueConfig { Name = "Folder", Value = otherFolder }],
        Children = [new ModuleConfig { Name = "Fields", Children = fields.ToList() }],
    };
}

void Rejects(ModuleConfig node, string message)
{
    try
    {
        ComponentLoader.Load([node]);
    }
    catch (InvalidOperationException)
    {
        checks++;
        return;
    }

    throw new InvalidOperationException("FAIL: 字段表配错应在装配时就报：" + message);
}

try
{
    // 0. 真 sc.xml 里配了工艺配方库节点和字段表；装配时 Folder 换成临时目录（不往运行目录写文件）。
    var scConfig = XmlHelper.Deserialize<ScConfig>(Path.Combine(AppContext.BaseDirectory, "Config", "sc.xml"));
    Check(scConfig is not null, "sc.xml 解析失败");
    var node = scConfig!.Modules.FirstOrDefault(setting => string.Equals(setting.Name, "ProcessRecipe", StringComparison.OrdinalIgnoreCase));
    Check(node is not null && node.Type == typeof(ProcessRecipeComponent).FullName, "sc.xml 里应有指向 ProcessRecipeComponent 的 ProcessRecipe 节点");
    string ValueOf(string name)
    {
        return node!.Values.First(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;
    }

    Check(ValueOf("Capacity") == "99" && ValueOf("Folder") == "Recipe\\Process" && ValueOf("NameMaxLength") == "32"
          && node!.Values.Count == 3, "sc.xml 里写全参数：个数 99、目录 Recipe\\Process、名称 32（上下限都挪进字段表了）");
    node!.Values.First(value => string.Equals(value.Name, "Folder", StringComparison.OrdinalIgnoreCase)).Value = folder;

    var roots = ComponentLoader.Load([node]);
    var library = roots.OfType<ProcessRecipeComponent>().Single();
    Check(library.Capacity == 99 && library.NameMaxLength == 32, "装出一个工艺配方库，参数按 sc.xml");
    Check(ReferenceEquals(ProcessRecipeComponent.Current, library), "装配出来即成为 Current");
    Check(Directory.Exists(folder) && library.List().Count == 0, "目录没有就建，开始是空的");
    var fields = library.Fields;
    Check(fields.Select(field => field.Key).SequenceEqual(new[] { "Seconds", "Rpm", "Arm", "Chemical", "Flow", "Mode", "Position", "ScanTo", "ScanSpeed" }),
        "字段表：9 个字段，按 sc.xml 的先后，实际 " + string.Join(",", fields.Select(field => field.Key)));
    ProcessRecipeField F(string key)
    {
        return fields.First(field => field.Key == key);
    }

    Check(F("Seconds").Type == ProcessRecipeFieldType.Double && F("Seconds").Required && F("Seconds").Min == 0.1 && F("Seconds").Max == 3600
          && F("Seconds").Decimals == 1 && F("Seconds").Default == "10" && F("Seconds").Unit == "s" && F("Seconds").Text == "时间" && F("Seconds").TextEn == "Time",
        "时间：小数、必填、0.1 ~ 3600 s、一位小数、默认 10");
    Check(F("Rpm").Type == ProcessRecipeFieldType.Int && F("Rpm").Min == 0 && F("Rpm").Max == 3000 && F("Rpm").Default == "500" && F("Rpm").Decimals is null,
        "转速：整数、0 ~ 3000、默认 500");
    Check(F("Arm").Type == ProcessRecipeFieldType.Choice && F("Arm").Source!.IsParts && F("Arm").Source!.PartType == "ArmAxisComponent"
          && F("Arm").Source!.Property.Length == 0 && !F("Arm").Required, "摆臂：下拉，从腔体里的摆臂轴取名字");
    Check(F("Chemical").Source!.PartType == "NozzleComponent" && F("Chemical").Source!.Property == "Chemical" && F("Chemical").Source!.ParentKey == "Arm",
        "药液：下拉，取所选摆臂下面喷嘴的 Chemical");
    Check(!F("Mode").Source!.IsParts && F("Mode").Source!.Options.SequenceEqual(new[] { "Time", "Scan" }) && F("Mode").Default == "Time",
        "方式：下拉，直接写的 Time、Scan，默认 Time");
    Check(F("Position").Unit.Length == 0 && F("Position").Min == 0 && F("Position").Max == 150, "位置：0 ~ 150，没单位");

    // 1. 字段表配错了装配时就抛（报清楚哪个节点），不留到编辑配方时才发现。
    Rejects(new ModuleConfig { Name = "ProcessRecipe", Type = typeof(ProcessRecipeComponent).FullName, Values = [new ValueConfig { Name = "Folder", Value = otherFolder }] },
        "没配 Fields");
    Rejects(LibraryNode(FieldNode("Rpm", ("Text", "转速"), ("Type", "Int"))), "没有 Seconds");
    Rejects(LibraryNode(FieldNode("Seconds", ("Text", "时间"), ("Type", "Int"))), "Seconds 不是小数");
    Rejects(LibraryNode(SecondsNode(), FieldNode("seconds", ("Text", "又一个"), ("Type", "Double"))), "字段名重复（不分大小写）");
    Rejects(LibraryNode(SecondsNode(), FieldNode("1st", ("Text", "第一"), ("Type", "Double"))), "字段名数字开头");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Rpm", ("Type", "Int"))), "没配 Text");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Rpm", ("Text", "转速"), ("Type", "Number"))), "类型写错");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Rpm", ("Text", "转速"), ("Type", "1"))), "类型写数字");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Rpm", ("Text", "转速"), ("Type", "Int"), ("Min", "10"), ("Max", "5"))), "下限比上限大");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Rpm", ("Text", "转速"), ("Type", "Int"), ("Min", "0.5"))), "整数字段的下限带小数");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Flow", ("Text", "流量"), ("Type", "Double"), ("Max", "abc"))), "上限不是数");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Flow", ("Text", "流量"), ("Type", "Double"), ("Decimals", "-1"))), "小数位是负的");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Flow", ("Text", "流量"), ("Type", "Double"), ("Required", "yes"))), "必填写成 yes");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Mode", ("Text", "方式"), ("Type", "Choice"))), "下拉没配数据源");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Mode", ("Text", "方式"), ("Type", "Choice"), ("Source", "Time,time"))), "选项写了两遍");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Arm", ("Text", "摆臂"), ("Type", "Choice"), ("Source", "Parts:Arm Axis"))), "部件类型写法不对");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Chem", ("Text", "药液"), ("Type", "Choice"), ("Source", "Parts:NozzleComponent.Chemical@Arm"))), "@ 跟的字段不存在");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Mode", ("Text", "方式"), ("Type", "Choice"), ("Source", "Time,Scan")),
        FieldNode("Chem", ("Text", "药液"), ("Type", "Choice"), ("Source", "Parts:NozzleComponent.Chemical@Mode"))), "@ 跟的是直接写选项的下拉");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Chem", ("Text", "药液"), ("Type", "Choice"), ("Source", "Parts:NozzleComponent.Chemical")),
        FieldNode("Flow", ("Text", "流量"), ("Type", "Choice"), ("Source", "Parts:NozzleComponent.Flow@Chem"))), "@ 跟的下拉取的是属性、不是部件名");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Rpm", ("Text", "转速"), ("Type", "Int"), ("Default", "abc"))), "默认值不是整数");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Rpm", ("Text", "转速"), ("Type", "Int"), ("Max", "3000"), ("Default", "5000"))), "默认值超过上限");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Flow", ("Text", "流量"), ("Type", "Double"), ("Decimals", "1"), ("Default", "1.25"))), "默认值小数位多了");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Mode", ("Text", "方式"), ("Type", "Choice"), ("Source", "Time,Scan"), ("Default", "Spin"))), "默认值不在选项里");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Flag", ("Text", "开关"), ("Type", "Bool"), ("Default", "yes"))), "开关的默认值不是 true / false");
    ProcessRecipeComponent.Current = library;

    // 2. 下拉按数据源取选项（模块全起来后 Bind）：从腔体部件取的按每个腔体取，几个腔体合起来给界面（同名不分大小写合并、按先后）；
    //    跟着摆臂走的药液按摆臂分开；没有喷嘴的摆臂也列（选了它药液就没得选）；合计时长的上限 = 腔体工艺超时（EC 没装取默认 600 s）。
    var pm1 = Chamber("SmokePM1", ("Arm1", ["DIW", "SC1"]), ("Arm2", []));
    var pm2 = Chamber("SmokePM2", ("arm1", ["diw", "HF"]), ("Arm3", ["SC1"]));
    library.Bind([pm1, pm2]);
    var arms = library.ChoicesOf(F("Arm"));
    Check(arms.Values.SequenceEqual(new[] { "Arm1", "Arm2", "Arm3" }), "摆臂：两个腔体合起来 Arm1、Arm2、Arm3，实际 " + string.Join(",", arms.Values));
    var chemicals = library.ChoicesOf(F("Chemical"));
    Check(chemicals.Values.Count == 0 && chemicals.ByParent["Arm1"].SequenceEqual(new[] { "DIW", "SC1", "HF" })
          && chemicals.ByParent["arm3"].SequenceEqual(new[] { "SC1" }) && chemicals.ByParent["Arm2"].Count == 0,
        "药液：按摆臂分开、两个腔体合起来、不分大小写去重");
    Check(library.ChoicesOf(F("Mode")).Values.SequenceEqual(new[] { "Time", "Scan" }) && library.ChoicesOf(F("Rpm")).Values.Count == 0,
        "直接写的选项照写的；不是下拉的没有选项");
    Check(library.MaxTotalSeconds == 600, "合计上限 = 腔体工艺超时 600 s");
    var wrongProperty = (ProcessRecipeComponent)ComponentLoader.Load([LibraryNode(SecondsNode(),
        FieldNode("Chem", ("Text", "药液"), ("Type", "Choice"), ("Source", "Parts:NozzleComponent.Liquid")))]).Single();
    try
    {
        wrongProperty.Bind([pm1]);
        Check(false, "数据源写的属性部件上没有，Bind 时应报出来");
    }
    catch (InvalidOperationException)
    {
        checks++;
    }

    ProcessRecipeComponent.Current = library;

    // 3. 新建：空编号上建，名称去掉首尾空白；先给一步（各字段的默认值），版本 1，记创建人；写成 001.xml，字段都写上（空的也写）。
    //    名称、编号不对的回错误码，不建、不发事件。
    var changed = new List<int>();
    library.Changed += index => changed.Add(index);
    var created = library.Create(1, "  SC1_60S ", "Tester");
    Check(created.IsOk && created.Recipe is not null && created.Recipe.Name == "SC1_60S" && created.Recipe.Revision == 1
          && created.Recipe.CreatedBy == "Tester" && created.Recipe.ModifiedBy == "Tester", "新建：版本 1，记创建人");
    var first = created.Recipe!.Steps.Single();
    Check(first.Values.Count == 9 && first.Get("Seconds") == "10" && first.Get("Rpm") == "500" && first.Get("Mode") == "Time" && first.Get("Arm").Length == 0,
        "新建先给一步：各字段的默认值（时间 10、转速 500、方式 Time，摆臂空着）");
    int Count(string text, string part)
    {
        return text.Split(part).Length - 1;
    }

    string firstFile = File.ReadAllText(Path.Combine(folder, "001.xml"));
    Check(Count(firstFile, "<Step ") == 1 && firstFile.Contains("Seconds=\"10\"") && firstFile.Contains("Arm=\"\"") && firstFile.Contains("ScanSpeed=\"\""),
        "一个编号一个文件 001.xml，字段都写成 Step 的属性（空的也写）");
    Check(changed.SequenceEqual(new[] { 1 }), "新建发一次变更事件");

    Fails(library.Create(1, "OTHER", "Tester"), ErrorCodes.ProcessRecipeIndexOccupied, ["1", "SC1_60S"], "编号已经有了");
    Fails(library.Create(2, "sc1_60s", "Tester"), ErrorCodes.ProcessRecipeNameDuplicate, ["sc1_60s", "1"], "名称重了（不分大小写）");
    Fails(library.Create(2, "SC1 60S", "Tester"), ErrorCodes.ProcessRecipeNameInvalid, ["SC1 60S"], "名称里有空格");
    Fails(library.Create(2, "药洗", "Tester"), ErrorCodes.ProcessRecipeNameInvalid, ["药洗"], "名称只能用字母、数字、_ 和 -");
    Fails(library.Create(2, "   ", "Tester"), ErrorCodes.ProcessRecipeNameRequired, [], "名称没填");
    Fails(library.Create(2, new string('A', 33), "Tester"), ErrorCodes.ProcessRecipeNameTooLong, ["32"], "名称超过 32 个字符");
    Fails(library.Create(0, "ZERO", "Tester"), ErrorCodes.ProcessRecipeIndexOutOfRange, ["0", "99"], "编号 0");
    Fails(library.Create(100, "HUNDRED", "Tester"), ErrorCodes.ProcessRecipeIndexOutOfRange, ["100", "99"], "编号超过个数");
    Check(changed.Count == 1 && library.List().Count == 1, "没成的不建、不发事件");

    // 4. 保存：版本对得上、按字段表检查通过才存，版本加 1、记修改人。规整：只留字段表里的字段、按字段表的先后，
    //    数字去掉多余的写法、下拉换成数据源里的写法（大小写）。
    var extra = Step("5", "+300", "arm1", "diw", "1.50", "time", "150");
    extra.Values.Add(new ProcessRecipeValue("Leftover", "x"));
    var good = new List<ProcessRecipeStep>
    {
        extra,
        Step("60", "500", "Arm1", "SC1", "1", "Scan", "150", "30", "20"),
        Step("20.0", "2000"),
    };
    var saved = library.Save(1, 1, " 药洗 ", good, "Saver");
    Check(saved.IsOk && saved.Recipe is not null && saved.Recipe.Revision == 2 && saved.Recipe.ModifiedBy == "Saver"
          && saved.Recipe.CreatedBy == "Tester" && saved.Recipe.Description == "药洗", "保存：版本 2，记修改人，说明去掉首尾空白");
    var savedSteps = saved.Recipe!.Steps;
    Check(savedSteps[0].Values.Select(value => value.Name).SequenceEqual(fields.Select(field => field.Key)),
        "规整：只留字段表里的字段（Leftover 丢掉）、按字段表的先后");
    Check(savedSteps[0].Get("Rpm") == "300" && savedSteps[0].Get("Arm") == "Arm1" && savedSteps[0].Get("Chemical") == "DIW"
          && savedSteps[0].Get("Flow") == "1.5" && savedSteps[0].Get("Mode") == "Time" && savedSteps[2].Get("Seconds") == "20",
        "规整：+300 存 300、1.50 存 1.5、20.0 存 20，摆臂、药液、方式按数据源的写法");
    Check(saved.Recipe.TotalSeconds == 85, "合计时长 = 各步时间加起来");
    string text = File.ReadAllText(Path.Combine(folder, "001.xml"));
    Check(Count(text, "<Step ") == 3 && Count(text, " Chemical=") == 3 && !text.Contains("Leftover"), "文件里每一步字段都写上，字段表里没有的不写");
    Check(changed.SequenceEqual(new[] { 1, 1 }), "保存发一次变更事件");

    var ok = Step("5", "300", "Arm1", "DIW", "1.5", "Time", "150");
    ProcessRecipeResult SaveOne(ProcessRecipeStep step)
    {
        return library.Save(1, 2, string.Empty, [ok, step], "Saver");
    }

    Fails(library.Save(1, 1, string.Empty, good, "Saver"), ErrorCodes.ProcessRecipeRevisionMismatch, ["1"], "旧版本存不进去（别处改过）");
    Fails(library.Save(1, 2, string.Empty, [], "Saver"), ErrorCodes.ProcessRecipeNoSteps, [], "一步都没有");
    Fails(SaveOne(Step(" ", "300")), ErrorCodes.ProcessRecipeValueRequired, ["2", "时间"], "必填的时间没填");
    Fails(SaveOne(Step("5", "")), ErrorCodes.ProcessRecipeValueRequired, ["2", "转速"], "必填的转速没填");
    Fails(SaveOne(Step("abc", "300")), ErrorCodes.ProcessRecipeValueNotNumber, ["2", "时间", "abc"], "时间不是数");
    Fails(SaveOne(Step("1e3", "300")), ErrorCodes.ProcessRecipeValueNotNumber, ["2", "时间", "1e3"], "不认科学计数法");
    Fails(SaveOne(Step("5", "1.5")), ErrorCodes.ProcessRecipeValueNotInteger, ["2", "转速", "1.5"], "转速不是整数");
    Fails(SaveOne(Step("0", "300")), ErrorCodes.ProcessRecipeValueBelowMin, ["2", "时间", "0.1", " s"], "时间比下限小（带单位）");
    Fails(SaveOne(Step("5", "3001")), ErrorCodes.ProcessRecipeValueAboveMax, ["2", "转速", "3000", " rpm"], "转速比上限大");
    Fails(SaveOne(Step("5", "-1")), ErrorCodes.ProcessRecipeValueBelowMin, ["2", "转速", "0", " rpm"], "转速是负的");
    Fails(SaveOne(Step("5", "300", "Arm1", "DIW", "1.5", "Time", "151")), ErrorCodes.ProcessRecipeValueAboveMax, ["2", "位置", "150", ""], "位置过了中心（没单位）");
    Fails(SaveOne(Step("5", "300", "Arm1", "DIW", "1.25")), ErrorCodes.ProcessRecipeValueTooPrecise, ["2", "流量", "1"], "流量多了一位小数");
    Fails(SaveOne(Step("5", "300", "Arm9")), ErrorCodes.ProcessRecipeValueNotInOptions, ["2", "摆臂", "Arm9"], "没有这条摆臂");
    Fails(SaveOne(Step("5", "300", "Arm3", "HF")), ErrorCodes.ProcessRecipeValueNotInOptions, ["2", "药液", "HF"], "药液不在所选摆臂上");
    Fails(SaveOne(Step("5", "300", "", "DIW")), ErrorCodes.ProcessRecipeValueNotInOptions, ["2", "药液", "DIW"], "没选摆臂就没有药液可选");
    Fails(SaveOne(Step("5", "300", "Arm2", "DIW")), ErrorCodes.ProcessRecipeValueNotInOptions, ["2", "药液", "DIW"], "没有喷嘴的摆臂下面没有药液");
    Fails(SaveOne(Step("5", "300", mode: "Spin")), ErrorCodes.ProcessRecipeValueNotInOptions, ["2", "方式", "Spin"], "方式只有 Time、Scan");
    Fails(library.Save(1, 2, string.Empty, [Step("600", "300"), Step("1.5", "300")], "Saver"), ErrorCodes.ProcessRecipeTotalTooLong, ["601.5", "600"], "合计超过腔体工艺超时");
    Fails(library.Save(7, 1, string.Empty, good, "Saver"), ErrorCodes.ProcessRecipeNotFound, ["7"], "没有的编号");
    Check(library.Get(1).Recipe?.Revision == 2 && changed.Count == 2, "没存成的不改库、不发事件");
    // 系统组件构造即成为 Current：界面语言切成英文
    _ = new SystemComponent { Language = "en-US" };
    Fails(SaveOne(Step(" ", "300")), ErrorCodes.ProcessRecipeValueRequired, ["2", "Time"], "英文界面时提示里用英文字段名");
    SystemComponent.Current = null;
    Check(SaveOne(Step("0.1", "0", "Arm3", "sc1", "0.1", "SCAN", "0", "150", "0.1")).IsOk, "边界值都能存：时间 0.1、转速 0、流量 0.1、位置 0 和 150、速度 0.1");
    Check(library.Get(1).Recipe!.Steps[1].Get("Mode") == "Scan" && library.Get(1).Recipe!.Steps[1].Get("Chemical") == "SC1", "下拉的写法跟数据源一样");

    // 5. 开关、文本字段（自己拼的字段表）：开关只认 true / false（存成小写），文本去掉首尾空白；字段之间不互相管。
    var flags = (ProcessRecipeComponent)ComponentLoader.Load([LibraryNode(SecondsNode(),
        FieldNode("Flag", ("Text", "开关"), ("Type", "Bool"), ("Default", "false")),
        FieldNode("Note", ("Text", "备注"), ("Type", "Text")))]).Single();
    Check(flags.Create(1, "FLAGS", "Tester").IsOk && flags.Get(1).Recipe!.Steps[0].Get("Flag") == "false", "开关默认 false");
    ProcessRecipeStep FlagStep(string flag, string note)
    {
        return new ProcessRecipeStep { Values = [new("Seconds", "1"), new("Flag", flag), new("Note", note)] };
    }

    var flagSaved = flags.Save(1, 1, string.Empty, [FlagStep("TRUE", "  hello  ")], "Tester");
    Check(flagSaved.IsOk && flagSaved.Recipe!.Steps[0].Get("Flag") == "true" && flagSaved.Recipe.Steps[0].Get("Note") == "hello", "开关存成小写，文本去掉首尾空白");
    Fails(flags.Save(1, 2, string.Empty, [FlagStep("yes", string.Empty)], "Tester"), ErrorCodes.ProcessRecipeValueNotInOptions, ["1", "开关", "yes"], "开关不认 yes");
    ProcessRecipeComponent.Current = library;

    // 6. 改名：版本加 1、记修改人；跟别的编号重了不行。
    Check(library.Create(2, "DIW_RINSE", "Tester").IsOk, "再建一个");
    Fails(library.Rename(2, "SC1_60S", "Tester"), ErrorCodes.ProcessRecipeNameDuplicate, ["SC1_60S", "1"], "改名跟别的编号重了");
    Fails(library.Rename(9, "NOBODY", "Tester"), ErrorCodes.ProcessRecipeNotFound, ["9"], "改没有的编号");
    var renamed = library.Rename(1, "SC1_45S", "Renamer");
    Check(renamed.IsOk && renamed.Recipe is not null && renamed.Recipe.Name == "SC1_45S" && renamed.Recipe.Revision == 4
          && renamed.Recipe.ModifiedBy == "Renamer" && renamed.Recipe.Steps.Count == 2, "改名：版本加 1，步骤不动");

    // 7. 名字查库（流程配方、腔体起工艺用）：不分大小写、去掉首尾空白。
    Check(library.Contains(" sc1_45s ") && !library.Contains("SC1_60S") && !library.Contains(string.Empty), "按名字查库");

    // 8. 文件读回来：新开一个库读同一个目录，内容、版本一样；老配方里没有的字段按默认值补，字段表里没有的先留着；
    //    读不出来的、文件名不是编号的、超出个数的都跳过。
    File.WriteAllText(Path.Combine(folder, "003.xml"),
        "<ProcessRecipe Name=\"OLD\" Revision=\"1\"><Step Seconds=\"5\" Rpm=\"300\" Retired=\"x\" /></ProcessRecipe>");
    File.WriteAllText(Path.Combine(folder, "005.xml"), "<ProcessRecipe Name=");
    File.WriteAllText(Path.Combine(folder, "notes.xml"), "<ProcessRecipe Name=\"NOTES\" />");
    File.WriteAllText(Path.Combine(folder, "120.xml"), "<ProcessRecipe Name=\"TOO_BIG\" />");
    var reopened = (ProcessRecipeComponent)ComponentLoader.Load([node]).Single();
    ProcessRecipeComponent.Current = library;
    var reloaded = reopened.List();
    Check(reloaded.Select(item => item.Index).SequenceEqual(new[] { 1, 2, 3 }), "坏文件、不是编号的、超出个数的都跳过");
    var back = reloaded[0];
    Check(back.Name == "SC1_45S" && back.Revision == 4 && back.CreatedBy == "Tester" && back.ModifiedBy == "Renamer"
          && back.Steps.Count == 2 && back.Steps[0].Get("Arm") == "Arm1" && back.Steps[0].Get("Chemical") == "DIW"
          && back.Steps[1].Get("Arm") == "Arm3" && back.Steps[1].Get("Mode") == "Scan" && back.Steps[1].Get("Position") == "0"
          && back.Steps[1].Get("ScanTo") == "150" && back.Steps[1].Get("ScanSpeed") == "0.1" && back.Steps[1].Get("Flow") == "0.1", "读回来跟存的一样");
    var old = reloaded[2].Steps.Single();
    Check(old.Get("Seconds") == "5" && old.Get("Mode") == "Time" && old.Get("Arm").Length == 0 && old.Has("ScanSpeed") && old.Get("Retired") == "x",
        "老配方里没有的字段按默认值补（方式 Time，摆臂空着），字段表里没有的先留着");
    var oldSaved = reopened.Save(3, 1, string.Empty, reloaded[2].Steps, "Tester");
    Check(oldSaved.IsOk && !oldSaved.Recipe!.Steps[0].Has("Retired") && !File.ReadAllText(Path.Combine(folder, "003.xml")).Contains("Retired"),
        "字段表里没有的，下次保存时丢掉");
    Check(!Directory.EnumerateFiles(folder, "*.tmp").Any(), "写完不留临时文件");
    library.Load();

    // 9. 给出去的都是副本：改了不影响库里的。
    var copy = library.Get(1).Recipe!;
    copy.Name = "HACKED";
    copy.Steps[0].Values.Clear();
    copy.Steps.Clear();
    Check(library.Get(1).Recipe?.Name == "SC1_45S" && library.Get(1).Recipe?.Steps.Count == 2 && library.Get(1).Recipe!.Steps[0].Values.Count == 9,
        "拿到的是副本");

    // 10. 配方用到具体腔体：配方里从腔体部件取的下拉，选的值这个腔体要有（几个腔体装的不一样时）；直接写的选项不分腔体。
    Check(library.Save(1, 4, string.Empty, [Step("5", "300", "Arm1", "HF", "1", "Time", "150")], "Saver").IsOk, "存一个用 HF 的（只有 SmokePM2 的 Arm1 有）");
    var noHf = library.FindMismatch(" sc1_45s ", "SmokePM1");
    Check(noHf is not null && noHf.Field == "药液" && noHf.Value == "HF", "SmokePM1 没有 HF：对不上，报字段和值");
    Check(library.FindMismatch("SC1_45S", "SmokePM2") is null, "SmokePM2 有 HF：对得上");
    Check(library.Save(2, 1, string.Empty, [Step("5", "300", "Arm3", "SC1")], "Saver").IsOk, "存一个用 Arm3 的（只有 SmokePM2 有）");
    var noArm3 = library.FindMismatch("DIW_RINSE", "SmokePM1");
    Check(noArm3 is not null && noArm3.Field == "摆臂" && noArm3.Value == "Arm3", "SmokePM1 没有 Arm3：先对不上的是摆臂");
    Check(library.FindMismatch("DIW_RINSE", "LoadPort1") is null && library.FindMismatch("NOPE", "SmokePM1") is null,
        "不是腔体的、库里没有的配方不在这里报");

    // 11. 删除：文件删掉、编号空出来；再删、再取都说没有；没成的不发事件。
    changed.Clear();
    Check(library.Delete(3, "Tester").IsOk && !File.Exists(Path.Combine(folder, "003.xml")) && library.List().Count == 2, "删除");
    Fails(library.Delete(3, "Tester"), ErrorCodes.ProcessRecipeNotFound, ["3"], "再删说没有");
    Fails(library.Get(3), ErrorCodes.ProcessRecipeNotFound, ["3"], "再取说没有");
    Fails(library.Get(0), ErrorCodes.ProcessRecipeIndexOutOfRange, ["0", "99"], "取编号 0");
    Check(changed.SequenceEqual(new[] { 3 }), "删除发一次变更事件");

    // 12. 工艺配方服务（直接调 gRPC 服务类，不起网络）：列表带个数、说明、合计时长；字段表带类型、范围、默认值和取好的下拉选项；
    //     步骤按"字段名 → 值"来回；错误码和参数原样回给界面；没带操作人记成 Unknown；没装库回 process_recipe.not_installed。
    var service = new ProcessRecipeService([]);
    var listDto = (await service.GetListAsync(new RpcRequest())).DeserializeData<ProcessRecipeListDto>();
    Check(listDto.Capacity == 99 && listDto.Items.Count == 2 && listDto.Items[0].Index == 1 && listDto.Items[0].Name == "SC1_45S"
          && listDto.Items[0].TotalSeconds == 5, "列表：个数 99，编号、名称、合计时长");
    var options = (await service.GetOptionsAsync(new RpcRequest())).DeserializeData<ProcessRecipeOptionsDto>();
    Check(options.Fields.Select(field => field.Key).SequenceEqual(fields.Select(field => field.Key)) && options.MaxTotalSeconds == 600,
        "字段表：9 个字段按先后，合计上限 600 s");
    var secondsDto = options.Fields[0];
    Check(secondsDto.Type == ProcessRecipeFieldType.Double && secondsDto.Text == "时间" && secondsDto.TextEn == "Time" && secondsDto.Unit == "s"
          && secondsDto.Min == 0.1 && secondsDto.Max == 3600 && secondsDto.Decimals == 1 && secondsDto.Default == "10" && secondsDto.Required,
        "字段表：时间的类型、名字、单位、上下限、小数位、默认值、必填");
    var chemicalDto = options.Fields.First(field => field.Key == "Chemical");
    Check(chemicalDto.Type == ProcessRecipeFieldType.Choice && chemicalDto.ParentKey == "Arm" && chemicalDto.Options.Count == 0
          && chemicalDto.OptionsByParent["Arm1"].SequenceEqual(new[] { "DIW", "SC1", "HF" }) && options.Fields.First(field => field.Key == "Arm").Options.Count == 3
          && options.Fields.First(field => field.Key == "Mode").Options.SequenceEqual(new[] { "Time", "Scan" }),
        "字段表：下拉带上取好的选项（药液按摆臂分开）");
    var one = (await service.GetAsync(new ProcessRecipeIndexRequest { Index = 1 })).DeserializeData<ProcessRecipeDto>();
    Check(one.Index == 1 && one.Revision == 5 && one.Steps.Count == 1 && one.Steps[0].Values["Chemical"] == "HF" && one.Steps[0].Values.Count == 9,
        "取一个：头信息和每一步的字段值");

    var createdBySvc = await service.CreateAsync(new ProcessRecipeCreateRequest { Index = 3, Name = "SVC_NEW" });
    var createdDto = createdBySvc.DeserializeData<ProcessRecipeDto>();
    Check(createdBySvc.Success && createdDto.Index == 3 && createdDto.CreatedBy == "Unknown" && createdDto.Steps.Count == 1
          && createdDto.Steps[0].Values["Rpm"] == "500", "服务新建：没带操作人记成 Unknown，第一步是默认值");
    List<ProcessRecipeStepDto> ServiceSteps(string flow)
    {
        return
        [
            new ProcessRecipeStepDto { Values = new() { ["Seconds"] = "30", ["Rpm"] = "800", ["Arm"] = "Arm1", ["Chemical"] = "DIW", ["Flow"] = flow, ["Mode"] = "Scan", ["Position"] = "150", ["ScanTo"] = "60", ["ScanSpeed"] = "30" } },
            new ProcessRecipeStepDto { Values = new() { ["Seconds"] = "20", ["Rpm"] = "2000" } },
        ];
    }

    var savedBySvc = await service.SaveAsync(new ProcessRecipeSaveRequest { Index = 3, Revision = 1, Description = "d", Operator = "Op", Steps = ServiceSteps("1.5") });
    var savedDto = savedBySvc.DeserializeData<ProcessRecipeDto>();
    Check(savedBySvc.Success && savedDto.Revision == 2 && savedDto.ModifiedBy == "Op" && savedDto.Steps[0].Values["ScanTo"] == "60"
          && savedDto.Steps[1].Values["Arm"].Length == 0, "服务保存：版本加 1，没给的字段存成空的");
    var stale = await service.SaveAsync(new ProcessRecipeSaveRequest { Index = 3, Revision = 1, Operator = "Op", Steps = ServiceSteps("1.5") });
    Check(!stale.Success && stale.Code == ErrorCodes.ProcessRecipeRevisionMismatch && stale.Args.SequenceEqual(new[] { "3" }), "版本冲突：错误码带编号");
    var badFlow = await service.SaveAsync(new ProcessRecipeSaveRequest { Index = 3, Revision = 2, Operator = "Op", Steps = ServiceSteps("9") });
    Check(!badFlow.Success && badFlow.Code == ErrorCodes.ProcessRecipeValueAboveMax && badFlow.Args.SequenceEqual(new[] { "1", "流量", "3", " L/min" }),
        "流量超了：错误码带步号、字段名、上限和单位");
    var duplicate = await service.RenameAsync(new ProcessRecipeRenameRequest { Index = 3, Name = "sc1_45s", Operator = "Op" });
    Check(!duplicate.Success && duplicate.Code == ErrorCodes.ProcessRecipeNameDuplicate && duplicate.Args.SequenceEqual(new[] { "sc1_45s", "1" }),
        "改名重了：错误码带名称和编号");

    // 13. 腔体起工艺：配方要在库里，选的这个腔体要有；都对得上的照常往下走（探针腔体不接动作，回动作被拒）。
    var chamberService = new ChamberService([pm1, pm2]);
    var missing = await chamberService.ProcessAsync(new ChamberProcessRequest { Module = "SmokePM1", Recipe = " NOPE " });
    Check(!missing.Success && missing.Code == ErrorCodes.ChamberRecipeNotFound && missing.Args.SequenceEqual(new[] { "SmokePM1", "NOPE" }),
        "腔体起工艺：配方不在库里，错误码带模块和配方名");
    var mismatch = await chamberService.ProcessAsync(new ChamberProcessRequest { Module = "SmokePM1", Recipe = "sc1_45s" });
    Check(!mismatch.Success && mismatch.Code == ErrorCodes.ChamberRecipeOptionMissing && mismatch.Args.SequenceEqual(new[] { "SmokePM1", "sc1_45s", "药液", "HF" }),
        "腔体起工艺：配方里的 HF 这个腔体没有，错误码带模块、配方、字段和值");
    var fits = await chamberService.ProcessAsync(new ChamberProcessRequest { Module = "SmokePM2", Recipe = "sc1_45s" });
    Check(fits.Code != ErrorCodes.ChamberRecipeOptionMissing && fits.Code != ErrorCodes.ChamberRecipeNotFound, "对得上的腔体不挡");
    var empty = await chamberService.ProcessAsync(new ChamberProcessRequest { Module = "SmokePM1", Recipe = "  " });
    Check(!empty.Success && empty.Code == ErrorCodes.RecipeRequired, "没填配方还是 chamber.recipe_required");

    Check((await service.DeleteAsync(new ProcessRecipeDeleteRequest { Index = 3, Operator = "Op" })).Success, "服务删除");
    var gone = await service.GetAsync(new ProcessRecipeIndexRequest { Index = 3 });
    Check(!gone.Success && gone.Code == ErrorCodes.ProcessRecipeNotFound && gone.Args.SequenceEqual(new[] { "3" }), "删了再取：没有");

    ProcessRecipeComponent.Current = null;
    var notInstalled = await service.GetListAsync(new RpcRequest());
    Check(!notInstalled.Success && notInstalled.Code == ErrorCodes.ProcessRecipeNotInstalled && notInstalled.Args.Count == 0,
        "没装库：process_recipe.not_installed");
    var notChecked = await chamberService.ProcessAsync(new ChamberProcessRequest { Module = "SmokePM1", Recipe = "NOPE" });
    Check(notChecked.Code != ErrorCodes.ChamberRecipeNotFound && notChecked.Code != ErrorCodes.ChamberRecipeOptionMissing,
        "没装库时腔体起工艺不查配方");
}
finally
{
    ProcessRecipeComponent.Current = null;
    SystemComponent.Current = null;
    foreach (string path in new[] { folder, otherFolder })
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

Console.WriteLine($"PASS: {checks} process recipe checks (sc.xml node and field table, field table mistakes rejected at load, dropdown options from fixed lists and chamber parts merged across chambers, create/rename/save/delete with every field rule, normalizing, every field written to the file with defaults filled in for old recipes, bad files skipped, revision conflicts, change events, recipes checked against a specific chamber, the process recipe service, and chamber process requiring a recipe that fits the chamber).");

/// <summary>
/// 探针用：生产里名字由装配器经 internal setter 设，这里反射设。
/// </summary>
static class Probe
{
    public static void Name(ComponentBase component, string name)
    {
        typeof(ComponentBase).GetProperty(nameof(ComponentBase.Name))!.SetValue(component, name);
        typeof(ComponentBase).GetProperty(nameof(ComponentBase.FullPath))!.SetValue(component, name);
    }
}

// 探针腔体：只给摆臂、喷嘴挂在下面，不连设备、不做动作（起工艺回 null = 动作被拒）。
sealed class ProbeChamber : BaseChamberModule
{
    public ProbeChamber(string name)
    {
        Probe.Name(this, name);
    }

    public override ModuleOperation? Home() => null;

    protected override ModuleOperation? ResetDevice() => null;

    protected override ModuleOperation? AbortDevice() => null;

    protected override ModuleOperation? StartProcess(string recipe) => null;
}
