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

// 工艺配方冒烟：sc.xml 节点和两张字段表（配方头、步骤）、字段表配错开机就报、步骤下拉的选项按腔体 Type 照 sc.xml 写的、配方头的 Type 和超时时间、新建 / 改名 / 保存 / 删除和按字段表的每一条检查、
// 规整（数字、开关、下拉的写法，只留字段表里的字段）、文件读写（字段都写进文件、老配方缺的字段按默认值补、坏文件跳过）、版本冲突、变更事件、
// 配方用到具体腔体时腔体照名字核对摆臂和药液、gRPC 服务的列表 / 字段表 / 错误码，腔体起工艺时配方要在库里、选的摆臂和药液这个腔体要有。
// 不连设备，文件写在临时目录，跑完删掉。
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("FAIL: " + message);
    }

    checks++;
}

void Fails(HandleResult result, string code, string[] args, string message)
{
    Check(!result.IsSuccess && result.ErrorMessage == code && result.Args.SequenceEqual(args),
        $"{message}，实际 {result.ErrorMessage} [{string.Join(",", result.Args)}]");
}

// 库操作成了带回的配方（HandleResult 的 Result）；没带的为 null
RecipeData? Data(HandleResult result)
{
    return result.Result as RecipeData;
}

// 一步：按 sc.xml 字段表的字段名给值，不给的就是空的
RecipeStep Step(string seconds, string rpm, string arm = "", string chemical = "", string flow = "", string mode = "",
    string position = "", string scanTo = "", string scanSpeed = "")
{
    var values = new List<RecipeValue>
    {
        new("Seconds", seconds), new("Rpm", rpm), new("Arm", arm), new("Chemical", chemical), new("Flow", flow),
        new("Mode", mode), new("Position", position), new("ScanTo", scanTo), new("ScanSpeed", scanSpeed),
    };
    return new RecipeStep { Values = values };
}

// 要保存的一份内容：配方头能填的项（Type、超时时间、说明）+ 步骤
RecipeData Content(string description, List<RecipeStep> steps, string type = "A", string timeout = "600")
{
    return new RecipeData { Type = type, Timeout = timeout, Description = description, Steps = steps };
}

// 探针腔体：下面按给的装摆臂轴，摆臂下面按药液装喷嘴（跟 sc.xml 里的层级一样）；Type 跟 sc.xml 里的腔体一样写 A
ProbeChamber Chamber(string name, params (string Arm, string[] Chemicals)[] arms)
{
    var chamber = new ProbeChamber(name) { Type = "A" };
    foreach (var (armName, chemicals) in arms)
    {
        var arm = new SwingArmComponent();
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

// 配方头字段表：固定的几项（跟 sc.xml 一样），extra 是自己加的项；without 是故意不写的那一项、wrongType 把那一项的类型写错（查配错用）
ModuleConfig HeadNode(string without = "", string wrongType = "", params ModuleConfig[] extra)
{
    var items = new List<ModuleConfig>();
    foreach (var (key, type) in new[]
             {
                 ("Index", "Int"), ("Name", "Text"), ("Type", "Choice"), ("Timeout", "Double"), ("CreatedBy", "Text"),
                 ("CreatedAt", "Text"), ("ModifiedBy", "Text"), ("ModifiedAt", "Text"), ("Revision", "Int"), ("Description", "Text"),
             })
    {
        if (key == without)
        {
            continue;
        }

        string written = key == wrongType ? "Int" : type;
        items.Add(key == "Timeout"
            ? FieldNode(key, ("Text", "超时时间"), ("Type", written), ("Unit", "s"), ("Min", "1"), ("Max", "3600"), ("Decimals", "0"), ("Default", "600"), ("Required", "true"))
            : FieldNode(key, ("Text", key), ("Type", written)));
    }

    items.AddRange(extra);
    return new ModuleConfig { Name = "Head", Children = items };
}

ModuleConfig LibraryWith(ModuleConfig head, params ModuleConfig[] fields)
{
    return new ModuleConfig
    {
        Name = "ProcessRecipe",
        Type = typeof(RecipeComponent).FullName,
        Values = [new ValueConfig { Name = "Folder", Value = otherFolder }],
        Children = [head, new ModuleConfig { Name = "Fields", Children = fields.ToList() }],
    };
}

ModuleConfig LibraryNode(params ModuleConfig[] fields)
{
    return LibraryWith(HeadNode(), fields);
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
    Check(node is not null && node.Type == typeof(RecipeComponent).FullName, "sc.xml 里应有指向 RecipeComponent 的 ProcessRecipe 节点");
    string ValueOf(string name)
    {
        return node!.Values.First(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;
    }

    Check(ValueOf("Capacity") == "99" && ValueOf("Folder") == "Recipe\\Process" && ValueOf("NameMaxLength") == "32"
          && node!.Values.Count == 3, "sc.xml 里写全参数：个数 99、目录 Recipe\\Process、名称 32（上下限都挪进字段表了）");
    node!.Values.First(value => string.Equals(value.Name, "Folder", StringComparison.OrdinalIgnoreCase)).Value = folder;

    var roots = ComponentLoader.Load([node]);
    var library = roots.OfType<RecipeComponent>().Single();
    Check(library.Capacity == 99 && library.NameMaxLength == 32, "装出一个工艺配方库，参数按 sc.xml");
    Check(ReferenceEquals(RecipeComponent.Current, library), "装配出来即成为 Current");
    Check(Directory.Exists(folder) && library.List().Count == 0, "目录没有就建，开始是空的");
    var fields = library.Fields;
    Check(fields.Select(field => field.Key).SequenceEqual(new[] { "Seconds", "Rpm", "Arm", "Chemical", "Flow", "Mode", "Position", "ScanTo", "ScanSpeed" }),
        "字段表：9 个字段，按 sc.xml 的先后，实际 " + string.Join(",", fields.Select(field => field.Key)));
    RecipeField F(string key)
    {
        return fields.First(field => field.Key == key);
    }

    Check(F("Seconds").Type == ProcessRecipeFieldType.Double && F("Seconds").Required && F("Seconds").Min == 0.1 && F("Seconds").Max == 3600
          && F("Seconds").Decimals == 1 && F("Seconds").Default == "10" && F("Seconds").Unit == "s" && F("Seconds").Text == "时间" && F("Seconds").TextEn == "Time",
        "时间：小数、必填、0.1 ~ 3600 s、一位小数、默认 10");
    Check(F("Rpm").Type == ProcessRecipeFieldType.Int && F("Rpm").Min == 0 && F("Rpm").Max == 3000 && F("Rpm").Default == "500" && F("Rpm").Decimals is null,
        "转速：整数、0 ~ 3000、默认 500");
    Check(F("Arm").Type == ProcessRecipeFieldType.Choice && F("Arm").OptionsFor("A").SequenceEqual(new[] { "Arm1" }) && !F("Arm").Required,
        "摆臂：下拉，Type A 的选项照 sc.xml 写的（腔体里摆臂的名字 Arm1）");
    Check(F("Chemical").Type == ProcessRecipeFieldType.Choice && F("Chemical").OptionsFor(" a ").SequenceEqual(new[] { "DIW", "SC1" })
          && F("Chemical").OptionsFor("B").Count == 0 && F("Chemical").OptionsFor(string.Empty).Count == 0,
        "药液：Type A 的选项 DIW、SC1（Type 不分大小写、去掉首尾空白）；没写的 Type 没有选项");
    Check(F("Mode").OptionsFor("A").SequenceEqual(new[] { "Time", "Scan" }) && F("Mode").Default == "Time" && F("Rpm").OptionsByType.Count == 0,
        "方式：下拉，Time、Scan，默认 Time；不是下拉的没有选项");
    Check(F("Position").Unit.Length == 0 && F("Position").Min == 0 && F("Position").Max == 150, "位置：0 ~ 150，没单位");
    var headFields = library.HeadFields;
    Check(headFields.Select(field => field.Key).SequenceEqual(new[]
          {
              "Index", "Name", "Type", "Timeout", "CreatedBy", "CreatedAt", "ModifiedBy", "ModifiedAt", "Revision", "Description",
          }), "配方头字段表：固定的 10 项，按 sc.xml 的先后（说明放最后），实际 " + string.Join(",", headFields.Select(field => field.Key)));
    var timeoutField = headFields.First(field => field.Key == "Timeout");
    Check(timeoutField.Type == ProcessRecipeFieldType.Double && timeoutField.Required && timeoutField.Min == 1 && timeoutField.Max == 3600
          && timeoutField.Decimals == 0 && timeoutField.Default == "600" && timeoutField.Unit == "s" && timeoutField.Text == "超时时间",
        "配方头超时时间：小数、必填、1 ~ 3600 s、整秒、默认 600");
    Check(headFields.First(field => field.Key == "Type").Type == ProcessRecipeFieldType.Choice
          && headFields.First(field => field.Key == "Type").OptionsByType.Count == 0
          && RecipeHead.IsAuto("revision") && !RecipeHead.IsAuto("Timeout") && RecipeHead.IsCustom("N2Purge"),
        "配方头 Type：下拉，选项不在 sc.xml 里写（是腔体配的 Type）；版本这类是程序算的，自己加的项不是固定项");
    Check(library.StationTypes.SequenceEqual(new[] { string.Empty }), "还没 Bind（没有腔体）时只有空 Type 一种");

    // 1. 字段表配错了装配时就抛（报清楚哪个节点），不留到编辑配方时才发现。
    Rejects(new ModuleConfig { Name = "ProcessRecipe", Type = typeof(RecipeComponent).FullName, Values = [new ValueConfig { Name = "Folder", Value = otherFolder }] },
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
    Rejects(LibraryNode(SecondsNode(), FieldNode("Mode", ("Text", "方式"), ("Type", "Choice"))), "下拉没写选项");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Mode", ("Text", "方式"), ("Type", "Choice"), ("Source", " , "))), "下拉的选项全是空的");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Mode", ("Text", "方式"), ("Type", "Choice"), ("Source", "Time,time"))), "选项写了两遍");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Rpm", ("Text", "转速"), ("Type", "Int"), ("Default", "abc"))), "默认值不是整数");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Rpm", ("Text", "转速"), ("Type", "Int"), ("Max", "3000"), ("Default", "5000"))), "默认值超过上限");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Flow", ("Text", "流量"), ("Type", "Double"), ("Decimals", "1"), ("Default", "1.25"))), "默认值小数位多了");
    // 下拉的默认值在不在选项里，要知道有哪几种 Type 才查得了：Bind 时报（还是开机就报）
    var badDefault = (RecipeComponent)ComponentLoader.Load([LibraryNode(SecondsNode(),
        FieldNode("Mode", ("Text", "方式"), ("Type", "Choice"), ("Source", "Time,Scan"), ("Default", "Spin")))]).Single();
    try
    {
        badDefault.Bind([]);
        Check(false, "下拉的默认值不在选项里：Bind 应报出来");
    }
    catch (InvalidOperationException)
    {
        checks++;
    }
    Rejects(LibraryNode(SecondsNode(), FieldNode("Flag", ("Text", "开关"), ("Type", "Bool"), ("Default", "yes"))), "开关的默认值不是 true / false");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Mode", ("Text", "方式"), ("Type", "Choice"), ("Source.A", "Time"), ("Source.a", "Scan"))),
        "同一种 Type 的选项写了两遍（不分大小写）");
    Rejects(LibraryNode(SecondsNode(), FieldNode("Mode", ("Text", "方式"), ("Type", "Choice"), ("Source.A", " "))), "某种 Type 的选项是空的");
    Rejects(LibraryWith(new ModuleConfig { Name = "NotHead" }, SecondsNode()), "没配 Head（配方头字段表）");
    Rejects(LibraryWith(HeadNode(without: "Timeout"), SecondsNode()), "配方头少了固定的超时时间");
    Rejects(LibraryWith(HeadNode(wrongType: "Type"), SecondsNode()), "配方头固定的 Type 类型写错（不是 Choice）");
    Rejects(LibraryWith(HeadNode(extra: FieldNode("name", ("Text", "又一个"), ("Type", "Text"))), SecondsNode()), "配方头字段名重复（不分大小写）");
    RecipeComponent.Current = library;

    // 2. 模块全起来后 Bind：记下腔体和它们配的 Type（配方头 Type 的选项）。三个探针腔体都是 Type A，装的不一样：
    //    SmokePM1 的 Arm1 上有 DIW、SC1；SmokePM2 的 arm1（名字大小写不同）上只有 diw；SmokePM3 只有 Arm2。
    //    有腔体是 Type B、字段表却没写 Type B 的选项：Bind 就抛（开机就报），库还是原来的样子。
    var pm1 = Chamber("SmokePM1", ("Arm1", ["DIW", "SC1"]), ("Arm2", []));
    var pm2 = Chamber("SmokePM2", ("arm1", ["diw"]));
    var pm3 = Chamber("SmokePM3", ("Arm2", ["DIW", "SC1"]));
    var pmB = Chamber("SmokePMB", ("Arm1", ["DIW", "SC1"]));
    pmB.Type = "B";
    try
    {
        library.Bind([pm1, pmB]);
        Check(false, "有 Type B 的腔体、字段表没写 Source.B：Bind 应报出来");
    }
    catch (InvalidOperationException)
    {
        checks++;
    }

    Check(library.StationTypes.SequenceEqual(new[] { string.Empty }), "Bind 没成：库还是原来的样子");
    library.Bind([pm1, pm2, pm3]);
    Check(library.StationTypes.SequenceEqual(new[] { "A" }), "Bind 之后：腔体的 Type 只有 A 一种");

    // 3. 新建：空编号上建，名称去掉首尾空白；配方头填默认值（Type 取腔体的第一种 A、超时时间 600），先给一步（各字段的默认值），
    //    版本 1，记创建人；写成 001.xml，字段都写上（空的也写）。名称、编号不对的回错误码，不建、不发事件。
    var changed = new List<int>();
    library.Changed += index => changed.Add(index);
    var created = library.Create(1, "  SC1_60S ", "Tester");
    Check(created.IsSuccess && Data(created) is not null && Data(created)!.Name == "SC1_60S" && Data(created)!.Revision == 1
          && Data(created)!.CreatedBy == "Tester" && Data(created)!.ModifiedBy == "Tester", "新建：版本 1，记创建人");
    Check(Data(created)!.Type == "A" && Data(created)!.Timeout == "600" && Data(created)!.TimeoutMilliseconds == 600000,
        "新建的配方头：Type 取腔体的第一种 A，超时时间默认 600 s");
    var first = Data(created)!.Steps.Single();
    Check(first.Values.Count == 9 && first.Get("Seconds") == "10" && first.Get("Rpm") == "500" && first.Get("Mode") == "Time" && first.Get("Arm").Length == 0,
        "新建先给一步：各字段的默认值（时间 10、转速 500、方式 Time，摆臂空着）");
    int Count(string text, string part)
    {
        return text.Split(part).Length - 1;
    }

    string firstFile = File.ReadAllText(Path.Combine(folder, "001.xml"));
    Check(Count(firstFile, "<Step ") == 1 && firstFile.Contains("Seconds=\"10\"") && firstFile.Contains("Arm=\"\"") && firstFile.Contains("ScanSpeed=\"\"")
          && firstFile.Contains("Type=\"A\"") && firstFile.Contains("Timeout=\"600\""),
        "一个编号一个文件 001.xml，配方头的 Type、超时时间写在根上，字段都写成 Step 的属性（空的也写）");
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
    //    数字去掉多余的写法、下拉换成选项里的写法（大小写）。
    var extra = Step("5", "+300", "arm1", "diw", "1.50", "time", "150");
    extra.Values.Add(new RecipeValue("Leftover", "x"));
    var good = new List<RecipeStep>
    {
        extra,
        Step("60", "500", "Arm1", "SC1", "1", "Scan", "150", "30", "20"),
        Step("20.0", "2000"),
    };
    var saved = library.Save(1, 1, Content(" 药洗 ", good, type: " a ", timeout: "300.0"), "Saver");
    Check(saved.IsSuccess && Data(saved) is not null && Data(saved)!.Revision == 2 && Data(saved)!.ModifiedBy == "Saver"
          && Data(saved)!.CreatedBy == "Tester" && Data(saved)!.Description == "药洗", "保存：版本 2，记修改人，说明去掉首尾空白");
    Check(Data(saved)!.Type == "A" && Data(saved)!.Timeout == "300", "规整配方头：Type 换成腔体配的写法（a → A），超时时间 300.0 存 300");
    var savedSteps = Data(saved)!.Steps;
    Check(savedSteps[0].Values.Select(value => value.Name).SequenceEqual(fields.Select(field => field.Key)),
        "规整：只留字段表里的字段（Leftover 丢掉）、按字段表的先后");
    Check(savedSteps[0].Get("Rpm") == "300" && savedSteps[0].Get("Arm") == "Arm1" && savedSteps[0].Get("Chemical") == "DIW"
          && savedSteps[0].Get("Flow") == "1.5" && savedSteps[0].Get("Mode") == "Time" && savedSteps[2].Get("Seconds") == "20",
        "规整：+300 存 300、1.50 存 1.5、20.0 存 20，摆臂、药液、方式按选项的写法");
    Check(Data(saved)!.TotalSeconds == 85, "合计时长 = 各步时间加起来");
    string text = File.ReadAllText(Path.Combine(folder, "001.xml"));
    Check(Count(text, "<Step ") == 3 && Count(text, " Chemical=") == 3 && !text.Contains("Leftover"), "文件里每一步字段都写上，字段表里没有的不写");
    Check(changed.SequenceEqual(new[] { 1, 1 }), "保存发一次变更事件");

    var ok = Step("5", "300", "Arm1", "DIW", "1.5", "Time", "150");
    HandleResult SaveOne(RecipeStep step)
    {
        return library.Save(1, 2, Content(string.Empty, [ok, step]), "Saver");
    }

    HandleResult SaveHead(string type, string timeout)
    {
        return library.Save(1, 2, Content(string.Empty, [ok], type, timeout), "Saver");
    }

    Fails(library.Save(1, 1, Content(string.Empty, good), "Saver"), ErrorCodes.ProcessRecipeRevisionMismatch, ["1"], "旧版本存不进去（别处改过）");
    Fails(library.Save(1, 2, Content(string.Empty, []), "Saver"), ErrorCodes.ProcessRecipeNoSteps, [], "一步都没有");
    Fails(SaveHead(" ", "600"), ErrorCodes.ProcessRecipeHeadValueRequired, ["Type"], "配方头 Type 没选（腔体都配了 Type，不能空着）");
    Fails(SaveHead("B", "600"), ErrorCodes.ProcessRecipeHeadValueNotInOptions, ["Type", "B"], "配方头 Type 不是腔体配的那几种");
    Fails(SaveHead("A", string.Empty), ErrorCodes.ProcessRecipeHeadValueRequired, ["超时时间"], "超时时间没填");
    Fails(SaveHead("A", "abc"), ErrorCodes.ProcessRecipeHeadValueNotNumber, ["超时时间", "abc"], "超时时间不是数");
    Fails(SaveHead("A", "0"), ErrorCodes.ProcessRecipeHeadValueBelowMin, ["超时时间", "1", " s"], "超时时间比下限小（带单位）");
    Fails(SaveHead("A", "3601"), ErrorCodes.ProcessRecipeHeadValueAboveMax, ["超时时间", "3600", " s"], "超时时间比上限大");
    Fails(SaveHead("A", "60.5"), ErrorCodes.ProcessRecipeHeadValueTooPrecise, ["超时时间", "0"], "超时时间只要整秒");
    Fails(library.Save(1, 2, Content(string.Empty, [Step("60", "300"), Step("1", "300")], timeout: "60"), "Saver"),
        ErrorCodes.ProcessRecipeTotalTooLong, ["61", "60"], "合计超过配方头的超时时间");
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
    Fails(SaveOne(Step("5", "300", "Arm2")), ErrorCodes.ProcessRecipeValueNotInOptions, ["2", "摆臂", "Arm2"], "摆臂不在选项里（腔体里装了也不行，选项照 sc.xml 写的）");
    Fails(SaveOne(Step("5", "300", "Arm1", "HF")), ErrorCodes.ProcessRecipeValueNotInOptions, ["2", "药液", "HF"], "药液不在选项里");
    Fails(SaveOne(Step("5", "300", mode: "Spin")), ErrorCodes.ProcessRecipeValueNotInOptions, ["2", "方式", "Spin"], "方式只有 Time、Scan");
    Fails(library.Save(1, 2, Content(string.Empty, [Step("600", "300"), Step("1.5", "300")]), "Saver"), ErrorCodes.ProcessRecipeTotalTooLong, ["601.5", "600"],
        "合计超过超时时间 600 s");
    Fails(library.Save(7, 1, Content(string.Empty, good), "Saver"), ErrorCodes.ProcessRecipeNotFound, ["7"], "没有的编号");
    Check(Data(library.Get(1))?.Revision == 2 && changed.Count == 2, "没存成的不改库、不发事件");
    // 系统组件构造即成为 Current：界面语言切成英文
    _ = new SystemModule { Language = "en-US" };
    Fails(SaveOne(Step(" ", "300")), ErrorCodes.ProcessRecipeValueRequired, ["2", "Time"], "英文界面时提示里用英文字段名");
    SystemModule.Current = null;
    Check(SaveOne(Step("0.1", "0", "arm1", "sc1", "0.1", "SCAN", "0", "150", "0.1")).IsSuccess, "边界值都能存：时间 0.1、转速 0、流量 0.1、位置 0 和 150、速度 0.1");
    Check(Data(library.Get(1))!.Steps[1].Get("Mode") == "Scan" && Data(library.Get(1))!.Steps[1].Get("Chemical") == "SC1"
          && Data(library.Get(1))!.Steps[1].Get("Arm") == "Arm1", "下拉的写法跟选项一样");

    // 4b. 晶圆尺寸：位置、到的上限在 sc.xml 里按尺寸分开写（Max.8 = 100、Max.12 = 150），整机切到哪个尺寸用哪个，没配晶圆尺寸组件按 12 寸；
    //     切尺寸写回 sc.xml（输出目录里链接过来的那份），只改 Size 那一个值、别的一个字节不动，切回来跟原来一模一样。
    var previousWaferSize = WaferSizeComponent.Current;
    WaferSizeComponent.Current = null;
    Check(F("Position").MaxFor(8) == 100 && F("Position").MaxFor(12) == 150 && F("ScanTo").MaxFor(8) == 100 && F("ScanTo").MaxFor(12) == 150
          && F("Seconds").MaxFor(8) == 3600 && F("Position").Max == 150,
        "位置、到按尺寸分开写上限：8 寸 100、12 寸 150；没分开写的两种尺寸一样；没配晶圆尺寸按 12 寸");
    string scFile = Path.Combine(AppContext.BaseDirectory, "Config", "sc.xml");
    byte[] scBefore = File.ReadAllBytes(scFile);
    try
    {
        var sizeRoots = ComponentLoader.Load(
        [
            new ModuleConfig
            {
                Name = "System",
                Children =
                [
                    new ModuleConfig
                    {
                        Name = "WaferSize",
                        Type = typeof(WaferSizeComponent).FullName,
                        Values = [new ValueConfig { Name = "Size", Value = "12" }],
                    },
                ],
            },
        ]);
        var waferSize = sizeRoots.OfType<WaferSizeComponent>().Single();
        int sizeEvents = 0;
        waferSize.Changed += _ => sizeEvents++;
        Check(WaferSizeComponent.Current == waferSize && waferSize.FullPath == "System.WaferSize" && waferSize.Size == 12,
            "晶圆尺寸组件挂在 System 下面，路径跟 sc.xml 的层级一样");
        Fails(waferSize.SetSize(6), ErrorCodes.WaferSizeInvalid, ["6"], "只能切 8 寸、12 寸");
        Check(waferSize.SetSize(8).IsSuccess && waferSize.Size == 8 && WaferSizeComponent.CurrentSize == 8 && sizeEvents == 1,
            "切到 8 寸：马上换上，发一次变更");
        const string size12Line = "<Value Name=\"Size\" Value=\"12\" />";
        const string size8Line = "<Value Name=\"Size\" Value=\"8\" />";
        string beforeText = System.Text.Encoding.UTF8.GetString(scBefore);
        byte[] scAfter = File.ReadAllBytes(scFile);
        Check(beforeText.Split(size12Line).Length == 2 && System.Text.Encoding.UTF8.GetString(scAfter) == beforeText.Replace(size12Line, size8Line)
              && scAfter.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }),
            "写回 sc.xml：只有 System 下 WaferSize 那一行的 Size 改成 8，别的一个字都没变，还是带 BOM 的 UTF-8");
        Check(F("Position").Max == 100 && F("ScanTo").Max == 100, "8 寸：位置、到的上限是 100");
        int revision = Data(library.Get(1))!.Revision;
        Fails(library.Save(1, revision, Content(string.Empty, [Step("5", "300"), Step("5", "300", "Arm1", "DIW", "1.5", "Time", "101")]), "Saver"),
            ErrorCodes.ProcessRecipeValueAboveMax, ["2", "位置", "100", ""], "8 寸：位置过了 8 寸的中心（库里不动）");
        Check(F("Position").CheckFormat("100", []) is null, "8 寸：位置 100（中心）合规");
        Check(waferSize.SetSize(8).IsSuccess && sizeEvents == 1, "跟现在一样直接成功，不发变更");
        Check(waferSize.SetSize(12).IsSuccess && sizeEvents == 2 && File.ReadAllBytes(scFile).SequenceEqual(scBefore),
            "切回 12 寸：sc.xml 跟原来一个字节都不差（缩进、换行、BOM 都没动）");

        // 节点下没写 Size（装配时用的默认 12）：切的时候照节点的缩进补一行，补出来跟手写的一样
        string lineBreak = beforeText.Contains("\r\n") ? "\r\n" : "\n";
        string withoutSize = beforeText.Replace(lineBreak + "\t\t\t" + size12Line, string.Empty);
        Check(withoutSize != beforeText, "造一份 WaferSize 下没写 Size 的 sc.xml");
        File.WriteAllText(scFile, withoutSize, new System.Text.UTF8Encoding(false));
        Check(waferSize.SetSize(8).IsSuccess && System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(scFile)) == beforeText.Replace(size12Line, size8Line),
            "没写 Size 的节点：补上一行 Size = 8，缩进跟别的行一样");
        Check(waferSize.SetSize(12).IsSuccess && File.ReadAllBytes(scFile).SequenceEqual(scBefore), "再切回 12 寸");

        // 换行照原文件：git 签出来是 CRLF、仓库里存的是 LF，两种来回切都一个字节不差
        foreach (string newLine in new[] { "\r\n", "\n" })
        {
            string sameText = beforeText.Replace("\r\n", "\n").Replace("\n", newLine);
            byte[] sameBytes = new System.Text.UTF8Encoding(false).GetBytes(sameText);
            File.WriteAllBytes(scFile, sameBytes);
            Check(waferSize.SetSize(8).IsSuccess && waferSize.SetSize(12).IsSuccess && File.ReadAllBytes(scFile).SequenceEqual(sameBytes),
                $"换行是 {(newLine.Length == 2 ? "CRLF" : "LF")} 的 sc.xml：切过去再切回来跟原来一样");
        }
    }
    finally
    {
        File.WriteAllBytes(scFile, scBefore);
        WaferSizeComponent.Current = previousWaferSize;
    }

    // 5. 开关、文本字段（自己拼的字段表）：开关只认 true / false（存成小写），文本去掉首尾空白；字段之间不互相管。
    var flags = (RecipeComponent)ComponentLoader.Load([LibraryNode(SecondsNode(),
        FieldNode("Flag", ("Text", "开关"), ("Type", "Bool"), ("Default", "false")),
        FieldNode("Note", ("Text", "备注"), ("Type", "Text")))]).Single();
    Check(flags.Create(1, "FLAGS", "Tester").IsSuccess && Data(flags.Get(1))!.Steps[0].Get("Flag") == "false", "开关默认 false");
    RecipeStep FlagStep(string flag, string note)
    {
        return new RecipeStep { Values = [new("Seconds", "1"), new("Flag", flag), new("Note", note)] };
    }

    var flagSaved = flags.Save(1, 1, Content(string.Empty, [FlagStep("TRUE", "  hello  ")], type: string.Empty), "Tester");
    Check(flagSaved.IsSuccess && Data(flagSaved)!.Steps[0].Get("Flag") == "true" && Data(flagSaved)!.Steps[0].Get("Note") == "hello", "开关存成小写，文本去掉首尾空白");
    Fails(flags.Save(1, 2, Content(string.Empty, [FlagStep("yes", string.Empty)], type: string.Empty), "Tester"), ErrorCodes.ProcessRecipeValueNotInOptions, ["1", "开关", "yes"], "开关不认 yes");
    RecipeComponent.Current = library;

    // 6. 改名：版本加 1、记修改人；跟别的编号重了不行。
    Check(library.Create(2, "DIW_RINSE", "Tester").IsSuccess, "再建一个");
    Fails(library.Rename(2, "SC1_60S", "Tester"), ErrorCodes.ProcessRecipeNameDuplicate, ["SC1_60S", "1"], "改名跟别的编号重了");
    Fails(library.Rename(9, "NOBODY", "Tester"), ErrorCodes.ProcessRecipeNotFound, ["9"], "改没有的编号");
    var renamed = library.Rename(1, "SC1_45S", "Renamer");
    Check(renamed.IsSuccess && Data(renamed) is not null && Data(renamed)!.Name == "SC1_45S" && Data(renamed)!.Revision == 4
          && Data(renamed)!.ModifiedBy == "Renamer" && Data(renamed)!.Steps.Count == 2, "改名：版本加 1，步骤不动");

    // 7. 名字查库（流程配方、腔体起工艺用）：不分大小写、去掉首尾空白。
    Check(library.Contains(" sc1_45s ") && !library.Contains("SC1_60S") && !library.Contains(string.Empty), "按名字查库");

    // 8. 文件读回来：新开一个库读同一个目录，内容、版本一样；老配方里没有的字段按默认值补，字段表里没有的先留着；
    //    老配方没写超时时间的读的时候补默认值，没写 Type 的 Bind 时补腔体的第一种；读不出来的、文件名不是编号的、超出个数的都跳过。
    File.WriteAllText(Path.Combine(folder, "003.xml"),
        "<ProcessRecipe Name=\"OLD\" Revision=\"1\"><Step Seconds=\"5\" Rpm=\"300\" Retired=\"x\" /></ProcessRecipe>");
    File.WriteAllText(Path.Combine(folder, "005.xml"), "<ProcessRecipe Name=");
    File.WriteAllText(Path.Combine(folder, "notes.xml"), "<ProcessRecipe Name=\"NOTES\" />");
    File.WriteAllText(Path.Combine(folder, "120.xml"), "<ProcessRecipe Name=\"TOO_BIG\" />");
    var reopened = (RecipeComponent)ComponentLoader.Load([node]).Single();
    RecipeComponent.Current = library;
    var reloaded = reopened.List();
    Check(reloaded.Select(item => item.Index).SequenceEqual(new[] { 1, 2, 3 }), "坏文件、不是编号的、超出个数的都跳过");
    var back = reloaded[0];
    Check(back.Name == "SC1_45S" && back.Revision == 4 && back.CreatedBy == "Tester" && back.ModifiedBy == "Renamer"
          && back.Steps.Count == 2 && back.Steps[0].Get("Arm") == "Arm1" && back.Steps[0].Get("Chemical") == "DIW"
          && back.Steps[1].Get("Arm") == "Arm1" && back.Steps[1].Get("Mode") == "Scan" && back.Steps[1].Get("Position") == "0"
          && back.Steps[1].Get("ScanTo") == "150" && back.Steps[1].Get("ScanSpeed") == "0.1" && back.Steps[1].Get("Flow") == "0.1", "读回来跟存的一样");
    Check(back.Type == "A" && back.Timeout == "600", "配方头读回来跟存的一样");
    var old = reloaded[2].Steps.Single();
    Check(old.Get("Seconds") == "5" && old.Get("Mode") == "Time" && old.Get("Arm").Length == 0 && old.Has("ScanSpeed") && old.Get("Retired") == "x",
        "老配方里没有的字段按默认值补（方式 Time，摆臂空着），字段表里没有的先留着");
    Check(reloaded[2].Type.Length == 0 && reloaded[2].Timeout == "600", "老配方读进来：超时时间补默认 600，Type 等 Bind 再补");
    reopened.Bind([pm1, pm2, pm3]);
    RecipeComponent.Current = library;
    var oldFilled = Data(reopened.Get(3))!;
    Check(oldFilled.Type == "A" && Data(reopened.Get(1))!.Type == "A", "Bind 之后老配方的 Type 补成腔体的第一种 A，写了的不动");
    var oldSaved = reopened.Save(3, 1, Content(string.Empty, oldFilled.Steps), "Tester");
    Check(oldSaved.IsSuccess && !Data(oldSaved)!.Steps[0].Has("Retired") && !File.ReadAllText(Path.Combine(folder, "003.xml")).Contains("Retired"),
        "字段表里没有的，下次保存时丢掉");
    Check(!Directory.EnumerateFiles(folder, "*.tmp").Any(), "写完不留临时文件");
    library.Load();

    // 9. 给出去的都是副本：改了不影响库里的。
    var copy = Data(library.Get(1))!;
    copy.Name = "HACKED";
    copy.Steps[0].Values.Clear();
    copy.Steps.Clear();
    Check(Data(library.Get(1))?.Name == "SC1_45S" && Data(library.Get(1))?.Steps.Count == 2 && Data(library.Get(1))!.Steps[0].Values.Count == 9,
        "拿到的是副本");

    // 10. 配方用到具体腔体：腔体照名字找配方里选的摆臂、药液（跟执行时一样，名字不分大小写），几个腔体装的不一样时才会对不上。
    //     字段之间不互相管：没选摆臂也能选药液，这一步不喷，药液用不上，哪个腔体都不查它。
    Check(library.Save(1, 4, Content(string.Empty, [Step("5", "300", "Arm1", "SC1", "1", "Time", "150")]), "Saver").IsSuccess, "存一个 Arm1 + SC1 的");
    Check(library.FindMismatch(" sc1_45s ", "SmokePM2") == ("药液", "SC1"), "SmokePM2 的 arm1 上没有 SC1：对不上，报字段和值");
    Check(library.FindMismatch("SC1_45S", " smokepm1 ") is null, "SmokePM1 的 Arm1 上有 SC1：对得上（腔体名不分大小写）");
    Check(library.FindMismatch("SC1_45S", "SmokePM3") == ("摆臂", "Arm1"), "SmokePM3 没有 Arm1：先对不上的是摆臂");
    Check(library.Save(2, 1, Content(string.Empty, [Step("5", "300", "Arm1", "DIW"), Step("5", "300", "", "SC1")]), "Saver").IsSuccess,
        "没选摆臂也能选药液：存得进去");
    Check(library.FindMismatch("DIW_RINSE", "SmokePM2") is null && library.FindMismatch("DIW_RINSE", "SmokePM3") is not null,
        "Arm1 + DIW：SmokePM2 的 arm1、diw 对得上（名字不分大小写），没选摆臂那一步的 SC1 不查；SmokePM3 没有 Arm1 对不上");
    Check(library.FindMismatch("DIW_RINSE", "LoadPort1") is null && library.FindMismatch("NOPE", "SmokePM1") is null,
        "不是腔体的、库里没有的配方不在这里报");
    Check(pmB.FindRecipeMismatch(library.Find("SC1_45S")!) == ("Type", "A"),
        "Type 为 B 的腔体：Type A 的配方先对不上的是 Type（摆臂、药液都有也不行）");
    var typeless = new RecipeData { Type = " b ", Steps = [Step("5", "300", "Arm1", "SC1")] };
    Check(pmB.FindRecipeMismatch(typeless) is null, "Type 不分大小写、去掉首尾空白");

    // 11. 删除：文件删掉、编号空出来；再删、再取都说没有；没成的不发事件。
    changed.Clear();
    Check(library.Delete(3, "Tester").IsSuccess && !File.Exists(Path.Combine(folder, "003.xml")) && library.List().Count == 2, "删除");
    Fails(library.Delete(3, "Tester"), ErrorCodes.ProcessRecipeNotFound, ["3"], "再删说没有");
    Fails(library.Get(3), ErrorCodes.ProcessRecipeNotFound, ["3"], "再取说没有");
    Fails(library.Get(0), ErrorCodes.ProcessRecipeIndexOutOfRange, ["0", "99"], "取编号 0");
    Check(changed.SequenceEqual(new[] { 3 }), "删除发一次变更事件");

    // 11b. Host 远程管配方（IRecipeComponent，EAP 的 S7 走它）：按名字列、取（JSON，每一步是字段名 → 值）、存（没有就新建、有就覆盖，
    //      检查、规整跟本地保存一样）、删；变化经 EAP 的派发组件报上报口。这一节建的最后删掉，后面接着是 1、2 两个。
    _ = new EapNotifierComponent();
    var eapChanges = new RecordingRecipeCallback();
    xyz.Components.Interfaces.IRecipeComponent remote = library;
    remote.E30Callback = eapChanges;
    Check(remote.RecipeNames.SequenceEqual(new[] { "SC1_45S", "DIW_RINSE" }), "按名字列（按编号）");
    string? exported = remote.GetRecipe("sc1_45s");
    Check(exported is not null && exported.Contains("\"values\"", StringComparison.Ordinal) && !exported.Contains("attributes", StringComparison.OrdinalIgnoreCase),
        "取出来是 JSON：每一步是字段名 → 值，不带存文件用的 XML 属性");
    Check(exported is not null && remote.AcceptsRecipe(exported) && !remote.AcceptsRecipe("{\"steps\":[{\"group\":\"LoadPort\",\"stations\":[]}]}")
          && !remote.AcceptsRecipe("{\"steps\":[]}") && !remote.AcceptsRecipe("{"), "认 JSON 的样子：每一步带字段值的是工艺配方；流程配方的样子、没有步骤、读不出来的都不认");
    var exportedData = exported is null ? null : JsonHelper.Deserialize<RecipeData>(exported);
    Check(exportedData is not null && exportedData.Steps.Count == 1 && exportedData.Steps[0].Values.Count == 9 && exportedData.Steps[0].Get("Chemical") == "SC1"
          && exportedData.Type == "A" && exportedData.Timeout == "600", "JSON 读回来跟库里一样（带配方头的 Type、超时时间）");

    var imported = remote.ImportRecipe("HOST_RECIPE", exported!, "Host");
    var hostRecipe = library.Find("HOST_RECIPE");
    Check(imported.IsSuccess && hostRecipe is not null && hostRecipe.Index == 3 && hostRecipe.Revision == 1 && hostRecipe.CreatedBy == "Host"
          && hostRecipe.Steps[0].Get("Chemical") == "SC1", "Host 下一个新名字：放在第一个空编号上，版本 1，建的人记 Host");
    string loose = JsonHelper.Serialize(new RecipeData
    {
        Name = "IGNORED", Revision = 77, Type = "a", Description = " 远程 ", Steps = [Step("5", "+300", "arm1", "diw", "1.50")],
    });
    var overwriteResult = remote.ImportRecipe("host_recipe", loose, "Host");
    var overwritten = library.Find("HOST_RECIPE");
    Check(overwriteResult.IsSuccess && overwritten is not null && overwritten.Name == "HOST_RECIPE" && overwritten.Revision == 2 && overwritten.Description == "远程"
          && overwritten.Steps[0].Get("Rpm") == "300" && overwritten.Steps[0].Get("Arm") == "Arm1" && overwritten.Steps[0].Get("Chemical") == "DIW"
          && overwritten.Type == "A" && overwritten.Timeout == "600",
        "同名再下：覆盖（版本加 1），跟本地保存一样规整写法；JSON 里的名字、版本不管，没带超时时间的补默认 600");
    var noType = remote.ImportRecipe("HOST_BAD", JsonHelper.Serialize(new RecipeData { Steps = [Step("5", "300")] }), "Host");
    Check(!noType.IsSuccess && noType.ErrorMessage == ErrorCodes.ProcessRecipeHeadValueRequired && library.Find("HOST_BAD") is null,
        "JSON 里没带 Type：不补、不收（不知道给哪种腔体用）");
    var tooLong = remote.ImportRecipe("HOST_BAD", JsonHelper.Serialize(new RecipeData { Type = "A", Steps = [Step("600", "300"), Step("1.5", "300")] }), "Host");
    Check(!tooLong.IsSuccess && tooLong.ErrorMessage == ErrorCodes.ProcessRecipeTotalTooLong && library.Find("HOST_BAD") is null,
        "步骤没过检查（合计超过超时时间）：跟本地保存一样拒，什么都不存");
    var notJson = remote.ImportRecipe("HOST_BAD", "{", "Host");
    Check(!notJson.IsSuccess && notJson.ErrorMessage == ErrorCodes.ProcessRecipeBodyInvalid && notJson.Args.SequenceEqual(new[] { "HOST_BAD" }),
        "内容不是 JSON：process_recipe.body_invalid 带名字");
    var nullValues = remote.ImportRecipe("HOST_BAD",
        "{\"type\":\"A\",\"head\":null,\"description\":null,\"steps\":[{\"values\":null},{\"values\":[{\"name\":null,\"value\":null}]}]}", "Host");
    Check(!nullValues.IsSuccess && nullValues.ErrorMessage == ErrorCodes.ProcessRecipeValueRequired, "JSON 里空着的值补成空的再查：必填的没填，不会出异常");
    var removedByHost = remote.DeleteRecipe("host_recipe", "Host");
    Check(removedByHost.IsSuccess && library.Find("HOST_RECIPE") is null && library.List().Count == 2, "按名字删（不分大小写）");
    Check(remote.DeleteRecipe("HOST_RECIPE", "Host").ErrorMessage == ErrorCodes.ProcessRecipeNameNotFound, "再删说没有：process_recipe.name_not_found");
    Check(eapChanges.WaitFor(3) && eapChanges.Changes.SequenceEqual(new[]
    {
        ("HOST_RECIPE", xyz.Components.Enums.ChangeKind.Created),
        ("HOST_RECIPE", xyz.Components.Enums.ChangeKind.Edited),
        ("HOST_RECIPE", xyz.Components.Enums.ChangeKind.Deleted),
    }), "上报口按先后收到新建、覆盖、删除；没成的不报");
    remote.E30Callback = null;

    // 12. 工艺配方服务（直接调 gRPC 服务类，不起网络）：列表带个数、说明、合计时长；字段表带类型、范围、默认值和下拉的选项；
    //     步骤按"字段名 → 值"来回；错误码和参数原样回给界面；没带操作人记成 Unknown；没装库回 process_recipe.not_installed。
    var service = new ProcessRecipeService([]);
    var listDto = (await service.GetListAsync(new RpcRequest())).DeserializeData<ProcessRecipeListDto>();
    Check(listDto.Capacity == 99 && listDto.Items.Count == 2 && listDto.Items[0].Index == 1 && listDto.Items[0].Name == "SC1_45S"
          && listDto.Items[0].TotalSeconds == 5, "列表：个数 99，编号、名称、合计时长");
    var options = (await service.GetOptionsAsync(new RpcRequest())).DeserializeData<ProcessRecipeOptionsDto>();
    Check(options.Fields.Select(field => field.Key).SequenceEqual(fields.Select(field => field.Key))
          && options.HeadFields.Select(field => field.Key).SequenceEqual(headFields.Select(field => field.Key)),
        "两张字段表：配方头 10 项、步骤 9 个字段，按先后");
    var typeDto = options.HeadFields.First(field => field.Key == "Type");
    Check(typeDto.Options.SequenceEqual(new[] { "A" }) && typeDto.Required && !typeDto.IsAuto
          && options.HeadFields.First(field => field.Key == "Revision").IsAuto && !options.HeadFields.First(field => field.Key == "Timeout").IsAuto,
        "配方头：Type 的选项是腔体配的 Type（A），都配了所以必选；版本是程序算的，超时时间要填");
    var secondsDto = options.Fields[0];
    Check(secondsDto.Type == ProcessRecipeFieldType.Double && secondsDto.Text == "时间" && secondsDto.TextEn == "Time" && secondsDto.Unit == "s"
          && secondsDto.Min == 0.1 && secondsDto.Max == 3600 && secondsDto.Decimals == 1 && secondsDto.Default == "10" && secondsDto.Required,
        "字段表：时间的类型、名字、单位、上下限、小数位、默认值、必填");
    var chemicalDto = options.Fields.First(field => field.Key == "Chemical");
    Check(chemicalDto.Type == ProcessRecipeFieldType.Choice && chemicalDto.OptionsByType["A"].SequenceEqual(new[] { "DIW", "SC1" })
          && options.Fields.First(field => field.Key == "Arm").OptionsByType["A"].SequenceEqual(new[] { "Arm1" })
          && options.Fields.First(field => field.Key == "Mode").OptionsByType["A"].SequenceEqual(new[] { "Time", "Scan" })
          && chemicalDto.Options.Count == 0,
        "步骤字段表：下拉带上 sc.xml 里按 Type 分开写的选项");
    var one = (await service.GetAsync(new ProcessRecipeIndexRequest { Index = 1 })).DeserializeData<ProcessRecipeDto>();
    Check(one.Index == 1 && one.Revision == 5 && one.Steps.Count == 1 && one.Steps[0].Values["Chemical"] == "SC1" && one.Steps[0].Values.Count == 9
          && one.Type == "A" && one.Timeout == "600", "取一个：配方头（Type、超时时间）和每一步的字段值");

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

    var savedBySvc = await service.SaveAsync(new ProcessRecipeSaveRequest
    {
        Index = 3, Revision = 1, Type = "A", Timeout = "120", Description = "d", Operator = "Op", Steps = ServiceSteps("1.5"),
    });
    var savedDto = savedBySvc.DeserializeData<ProcessRecipeDto>();
    Check(savedBySvc.Success && savedDto.Revision == 2 && savedDto.ModifiedBy == "Op" && savedDto.Steps[0].Values["ScanTo"] == "60"
          && savedDto.Steps[1].Values["Arm"].Length == 0 && savedDto.Type == "A" && savedDto.Timeout == "120",
        "服务保存：版本加 1，配方头的 Type、超时时间存上，没给的字段存成空的");
    var stale = await service.SaveAsync(new ProcessRecipeSaveRequest { Index = 3, Revision = 1, Type = "A", Timeout = "600", Operator = "Op", Steps = ServiceSteps("1.5") });
    Check(!stale.Success && stale.Code == ErrorCodes.ProcessRecipeRevisionMismatch && stale.Args.SequenceEqual(new[] { "3" }), "版本冲突：错误码带编号");
    var badFlow = await service.SaveAsync(new ProcessRecipeSaveRequest { Index = 3, Revision = 2, Type = "A", Timeout = "600", Operator = "Op", Steps = ServiceSteps("9") });
    Check(!badFlow.Success && badFlow.Code == ErrorCodes.ProcessRecipeValueAboveMax && badFlow.Args.SequenceEqual(new[] { "1", "流量", "3", " L/min" }),
        "流量超了：错误码带步号、字段名、上限和单位");
    var badType = await service.SaveAsync(new ProcessRecipeSaveRequest { Index = 3, Revision = 2, Type = "B", Timeout = "600", Operator = "Op", Steps = ServiceSteps("1.5") });
    Check(!badType.Success && badType.Code == ErrorCodes.ProcessRecipeHeadValueNotInOptions && badType.Args.SequenceEqual(new[] { "Type", "B" }),
        "Type 不是腔体配的：配方头的错误码带名字和值");
    var duplicate = await service.RenameAsync(new ProcessRecipeRenameRequest { Index = 3, Name = "sc1_45s", Operator = "Op" });
    Check(!duplicate.Success && duplicate.Code == ErrorCodes.ProcessRecipeNameDuplicate && duplicate.Args.SequenceEqual(new[] { "sc1_45s", "1" }),
        "改名重了：错误码带名称和编号");

    // 13. 腔体起工艺：配方要在库里，Type 要跟腔体一样，选的摆臂、药液这个腔体要有；都对得上的照常往下走（探针腔体不接动作，回动作被拒）。
    var chamberService = new ChamberService([pm1, pm2, pmB]);
    var wrongType = await chamberService.ProcessAsync(new ChamberProcessRequest { Module = "SmokePMB", Recipe = "sc1_45s" });
    Check(!wrongType.Success && wrongType.Code == ErrorCodes.ChamberRecipeOptionMissing && wrongType.Args.SequenceEqual(new[] { "SmokePMB", "sc1_45s", "Type", "A" }),
        "腔体起工艺：配方是 Type A、腔体是 Type B，错误码带模块、配方、字段 Type 和配方的 Type");
    var missing = await chamberService.ProcessAsync(new ChamberProcessRequest { Module = "SmokePM1", Recipe = " NOPE " });
    Check(!missing.Success && missing.Code == ErrorCodes.ChamberRecipeNotFound && missing.Args.SequenceEqual(new[] { "SmokePM1", "NOPE" }),
        "腔体起工艺：配方不在库里，错误码带模块和配方名");
    var mismatch = await chamberService.ProcessAsync(new ChamberProcessRequest { Module = "SmokePM2", Recipe = "sc1_45s" });
    Check(!mismatch.Success && mismatch.Code == ErrorCodes.ChamberRecipeOptionMissing && mismatch.Args.SequenceEqual(new[] { "SmokePM2", "sc1_45s", "药液", "SC1" }),
        "腔体起工艺：配方里的 SC1 这个腔体的摆臂上没有，错误码带模块、配方、字段和值");
    var fits = await chamberService.ProcessAsync(new ChamberProcessRequest { Module = "SmokePM1", Recipe = "sc1_45s" });
    Check(fits.Code != ErrorCodes.ChamberRecipeOptionMissing && fits.Code != ErrorCodes.ChamberRecipeNotFound, "对得上的腔体不挡");
    var empty = await chamberService.ProcessAsync(new ChamberProcessRequest { Module = "SmokePM1", Recipe = "  " });
    Check(!empty.Success && empty.Code == ErrorCodes.RecipeRequired, "没填配方还是 chamber.recipe_required");

    Check((await service.DeleteAsync(new ProcessRecipeDeleteRequest { Index = 3, Operator = "Op" })).Success, "服务删除");
    var gone = await service.GetAsync(new ProcessRecipeIndexRequest { Index = 3 });
    Check(!gone.Success && gone.Code == ErrorCodes.ProcessRecipeNotFound && gone.Args.SequenceEqual(new[] { "3" }), "删了再取：没有");

    RecipeComponent.Current = null;
    var notInstalled = await service.GetListAsync(new RpcRequest());
    Check(!notInstalled.Success && notInstalled.Code == ErrorCodes.ProcessRecipeNotInstalled && notInstalled.Args.Count == 0,
        "没装库：process_recipe.not_installed");
    var notChecked = await chamberService.ProcessAsync(new ChamberProcessRequest { Module = "SmokePM1", Recipe = "NOPE" });
    Check(!notChecked.Success && notChecked.Code == ErrorCodes.ChamberRecipeStepsMissing && notChecked.Args.SequenceEqual(new[] { "SmokePM1", "NOPE" }),
        "没装库时只有配方名、拿不到步骤：腔体起工艺前就挡下（chamber.recipe_steps_missing）");
}
finally
{
    RecipeComponent.Current = null;
    SystemModule.Current = null;
    foreach (string path in new[] { folder, otherFolder })
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

Console.WriteLine($"PASS: {checks} process recipe checks (sc.xml node with the recipe header and step field tables, options written per station Type, recipe Type and timeout in the header, host recipe management by name (JSON export without the XML-only attributes, import as new or overwrite through the same checks, delete, change reports), field table mistakes rejected at load, dropdown options exactly as written in sc.xml, create/rename/save/delete with every field rule, normalizing, every field written to the file with defaults filled in for old recipes, bad files skipped, revision conflicts, change events, recipes checked against a specific chamber's arms and nozzles by name, the process recipe service, and chamber process requiring a recipe that fits the chamber).");

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

    protected override ModuleOperation? CreateProcessOperation(ProcessRequest request) => null;
}

// 记下配方库报上来的变化（经 EAP 的派发组件在别的线程上调），等到够数为止。
sealed class RecordingRecipeCallback : xyz.Components.Interfaces.IE30Callback
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(string Name, xyz.Components.Enums.ChangeKind Change)> _changes = new();

    public IEnumerable<(string Name, xyz.Components.Enums.ChangeKind Change)> Changes => _changes;

    public void ProcessRecipeChanged(string name, xyz.Components.Enums.ChangeKind change)
    {
        _changes.Enqueue((name, change));
    }

    public void SequenceChanged(string name, xyz.Components.Enums.ChangeKind change)
    {
        throw new InvalidOperationException("Unexpected recipe callback");
    }

    public bool WaitFor(int count, int timeoutMs = 3000)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (_changes.Count >= count)
            {
                Thread.Sleep(100);
                return _changes.Count == count;
            }

            Thread.Sleep(20);
        }

        return false;
    }
}
