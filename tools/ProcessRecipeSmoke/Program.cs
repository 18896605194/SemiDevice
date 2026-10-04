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

// 工艺配方冒烟：sc.xml 节点和参数、能选的摆臂和药液（按腔体下装的摆臂轴和喷嘴生成）、新建 / 改名 / 保存 / 删除和每一条检查、
// 规整（摆臂、药液按 sc 的写法，用不上的字段清掉、不写进文件）、文件读写（坏文件跳过）、版本冲突、变更事件、
// gRPC 服务的列表 / 选项 / 错误码，腔体起工艺时配方要在库里。不连设备，文件写在临时目录，跑完删掉。
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

ProcessRecipeStep Step(double seconds, int rpm, string arm = "", string chemical = "", double flow = 0,
    ProcessArmMode mode = ProcessArmMode.Time, double position = 0, double scanTo = 0, double scanSpeed = 0)
{
    return new ProcessRecipeStep
    {
        Seconds = seconds,
        Rpm = rpm,
        Arm = arm,
        Chemical = chemical,
        Flow = flow,
        Mode = mode,
        Position = position,
        ScanTo = scanTo,
        ScanSpeed = scanSpeed,
    };
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

string folder = Path.Combine(Path.GetTempPath(), "xyz-process-recipe-smoke-" + Guid.NewGuid().ToString("N"));
try
{
    // 0. 真 sc.xml 里配了工艺配方库节点，参数都写全；装配时 Folder 换成临时目录（不往运行目录写文件）。
    var scConfig = XmlHelper.Deserialize<ScConfig>(Path.Combine(AppContext.BaseDirectory, "Config", "sc.xml"));
    Check(scConfig is not null, "sc.xml 解析失败");
    var node = scConfig!.Modules.FirstOrDefault(setting => string.Equals(setting.Name, "ProcessRecipe", StringComparison.OrdinalIgnoreCase));
    Check(node is not null && node.Type == typeof(ProcessRecipeComponent).FullName, "sc.xml 里应有指向 ProcessRecipeComponent 的 ProcessRecipe 节点");
    string ValueOf(string name)
    {
        return node!.Values.First(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;
    }

    Check(ValueOf("Capacity") == "99" && ValueOf("Folder") == "Recipe\\Process" && ValueOf("NameMaxLength") == "32"
          && ValueOf("MaxStepSeconds") == "3600" && ValueOf("MaxRpm") == "3000" && ValueOf("MaxFlow") == "3" && ValueOf("MaxScanSpeed") == "100",
        "sc.xml 里写全参数：个数 99、目录 Recipe\\Process、名称 32、一步 3600 s、转速 3000、流量 3、扫描速度 100");
    node!.Values.First(value => string.Equals(value.Name, "Folder", StringComparison.OrdinalIgnoreCase)).Value = folder;

    var roots = ComponentLoader.Load([node]);
    var library = roots.OfType<ProcessRecipeComponent>().Single();
    Check(library.Capacity == 99 && library.NameMaxLength == 32 && library.MaxStepSeconds == 3600 && library.MaxRpm == 3000
          && library.MaxFlow == 3 && library.MaxScanSpeed == 100, "装出一个工艺配方库，参数按 sc.xml");
    Check(ReferenceEquals(ProcessRecipeComponent.Current, library), "装配出来即成为 Current");
    Check(Directory.Exists(folder) && library.List().Count == 0, "目录没有就建，开始是空的");

    // 1. 能选的摆臂和药液：按腔体下的摆臂轴和喷嘴，几个腔体同名摆臂合成一条（药液取并集、按先后）；没有喷嘴的摆臂不列；
    //    合计时长的上限 = 腔体工艺超时（EC 没装取默认 600 s）。
    var pm1 = Chamber("SmokePM1", ("Arm1", ["DIW", "SC1"]), ("Arm2", []));
    var pm2 = Chamber("SmokePM2", ("arm1", ["diw", "HF"]), ("Arm3", ["SC1"]));
    library.Bind([pm1, pm2]);
    var arms = library.Arms;
    Check(arms.Select(arm => arm.Name).SequenceEqual(new[] { "Arm1", "Arm3" }), "摆臂：Arm1、Arm3（Arm2 没喷嘴不列），实际 " + string.Join(",", arms.Select(arm => arm.Name)));
    Check(arms[0].Chemicals.SequenceEqual(new[] { "DIW", "SC1", "HF" }) && arms[1].Chemicals.SequenceEqual(new[] { "SC1" }),
        "药液：同名摆臂合成一条、不分大小写去重、按先后");
    Check(library.MaxTotalSeconds == 600, "合计上限 = 腔体工艺超时 600 s");

    // 2. 新建：空编号上建，名称去掉首尾空白；先给一步（不出液），版本 1，记创建人；写成 001.xml。名称、编号不对的回错误码，不建、不发事件。
    var changed = new List<int>();
    library.Changed += index => changed.Add(index);
    var created = library.Create(1, "  SC1_60S ", "Tester");
    Check(created.IsOk && created.Recipe is not null && created.Recipe.Name == "SC1_60S" && created.Recipe.Revision == 1
          && created.Recipe.CreatedBy == "Tester" && created.Recipe.ModifiedBy == "Tester", "新建：版本 1，记创建人");
    Check(created.Recipe!.Steps.Count == 1 && created.Recipe.Steps[0].Arm == string.Empty && created.Recipe.Steps[0].Seconds > 0,
        "新建先给一步，不出液");
    Check(File.Exists(Path.Combine(folder, "001.xml")), "一个编号一个文件：001.xml");
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

    // 3. 保存：版本对得上、步骤检查通过才存，版本加 1、记修改人；摆臂、药液按 sc 的写法，用不上的字段清掉。
    var good = new List<ProcessRecipeStep>
    {
        Step(5, 300, "arm1", "diw", 1.5, ProcessArmMode.Time, 150, scanTo: 40, scanSpeed: 9),
        Step(60, 500, "Arm1", "SC1", 1.0, ProcessArmMode.Scan, 150, 30, 20),
        Step(20, 2000, " ", "LEFTOVER", 9, ProcessArmMode.Scan, 77, 66, 55),
    };
    var saved = library.Save(1, 1, " 药洗 ", good, "Saver");
    Check(saved.IsOk && saved.Recipe is not null && saved.Recipe.Revision == 2 && saved.Recipe.ModifiedBy == "Saver"
          && saved.Recipe.CreatedBy == "Tester" && saved.Recipe.Description == "药洗", "保存：版本 2，记修改人，说明去掉首尾空白");
    var savedSteps = saved.Recipe!.Steps;
    Check(savedSteps[0].Arm == "Arm1" && savedSteps[0].Chemical == "DIW" && savedSteps[0].ScanTo == 0 && savedSteps[0].ScanSpeed == 0,
        "规整：摆臂、药液按 sc 的写法；Time 清掉 Scan 的另一头和速度");
    Check(savedSteps[1].Mode == ProcessArmMode.Scan && savedSteps[1].ScanTo == 30 && savedSteps[1].ScanSpeed == 20, "Scan 的两头和速度留着");
    Check(savedSteps[2].Arm == string.Empty && savedSteps[2].Chemical == string.Empty && savedSteps[2].Flow == 0
          && savedSteps[2].Mode == ProcessArmMode.Time && savedSteps[2].Position == 0 && savedSteps[2].ScanTo == 0,
        "规整：不出液的清掉药液、流量、方式、位置");
    Check(saved.Recipe.TotalSeconds == 85, "合计时长 = 各步时间加起来");
    int Count(string text, string part)
    {
        return text.Split(part).Length - 1;
    }

    var text = File.ReadAllText(Path.Combine(folder, "001.xml"));
    Check(Count(text, "<Step ") == 3 && Count(text, " Arm=") == 2 && Count(text, " Chemical=") == 2 && Count(text, " Flow=") == 2
          && Count(text, " Position=") == 2 && Count(text, " ScanTo=") == 1 && Count(text, " ScanSpeed=") == 1,
        "用不上的字段不写进文件：不出液的没有药液、流量、位置，Time 没有 Scan 的另一头和速度");
    Check(changed.SequenceEqual(new[] { 1, 1 }), "保存发一次变更事件");

    var bad = Step(5, 300, "Arm1", "DIW", 1.5, ProcessArmMode.Time, 150);
    ProcessRecipeResult SaveOne(ProcessRecipeStep step)
    {
        return library.Save(1, 2, string.Empty, [bad, step], "Saver");
    }

    Fails(library.Save(1, 1, string.Empty, good, "Saver"), ErrorCodes.ProcessRecipeRevisionMismatch, ["1"], "旧版本存不进去（别处改过）");
    Fails(library.Save(1, 2, string.Empty, [], "Saver"), ErrorCodes.ProcessRecipeNoSteps, [], "一步都没有");
    Fails(SaveOne(Step(0, 300)), ErrorCodes.ProcessRecipeTimeOutOfRange, ["2", "0.1", "3600"], "时间 0");
    Fails(SaveOne(Step(3600.5, 300)), ErrorCodes.ProcessRecipeTimeOutOfRange, ["2", "0.1", "3600"], "时间超过一步上限");
    Fails(SaveOne(Step(double.NaN, 300)), ErrorCodes.ProcessRecipeTimeOutOfRange, ["2", "0.1", "3600"], "时间不是数");
    Fails(SaveOne(Step(5, -1)), ErrorCodes.ProcessRecipeRpmOutOfRange, ["2", "3000"], "转速是负的");
    Fails(SaveOne(Step(5, 3001)), ErrorCodes.ProcessRecipeRpmOutOfRange, ["2", "3000"], "转速超过上限");
    Fails(SaveOne(Step(5, 300, "Arm2", "DIW", 1)), ErrorCodes.ProcessRecipeArmNotFound, ["2", "Arm2"], "没有喷嘴的摆臂不能选");
    Fails(SaveOne(Step(5, 300, "Arm9", "DIW", 1)), ErrorCodes.ProcessRecipeArmNotFound, ["2", "Arm9"], "没有这条摆臂");
    Fails(SaveOne(Step(5, 300, "Arm1", " ", 1)), ErrorCodes.ProcessRecipeChemicalRequired, ["2", "Arm1"], "选了摆臂没选药液");
    Fails(SaveOne(Step(5, 300, "Arm3", "HF", 1)), ErrorCodes.ProcessRecipeChemicalNotOnArm, ["2", "HF", "Arm3"], "药液不在这条摆臂上");
    Fails(SaveOne(Step(5, 300, "Arm1", "DIW", 0)), ErrorCodes.ProcessRecipeFlowOutOfRange, ["2", "0.1", "3"], "流量 0");
    Fails(SaveOne(Step(5, 300, "Arm1", "DIW", 3.5)), ErrorCodes.ProcessRecipeFlowOutOfRange, ["2", "0.1", "3"], "流量超过上限");
    Fails(SaveOne(Step(5, 300, "Arm1", "DIW", 1, ProcessArmMode.Time, 151)), ErrorCodes.ProcessRecipePositionOutOfRange, ["2", "0", "150"], "位置过了中心");
    Fails(SaveOne(Step(5, 300, "Arm1", "DIW", 1, ProcessArmMode.Time, -1)), ErrorCodes.ProcessRecipePositionOutOfRange, ["2", "0", "150"], "位置出了边缘");
    Fails(SaveOne(Step(5, 300, "Arm1", "DIW", 1, ProcessArmMode.Scan, 150, 160, 20)), ErrorCodes.ProcessRecipePositionOutOfRange, ["2", "0", "150"], "Scan 另一头出了晶圆");
    Fails(SaveOne(Step(5, 300, "Arm1", "DIW", 1, ProcessArmMode.Scan, 100, 100, 20)), ErrorCodes.ProcessRecipeScanSamePosition, ["2"], "Scan 两头一样");
    Fails(SaveOne(Step(5, 300, "Arm1", "DIW", 1, ProcessArmMode.Scan, 150, 30, 0)), ErrorCodes.ProcessRecipeScanSpeedOutOfRange, ["2", "0.1", "100"], "Scan 速度 0");
    Fails(SaveOne(Step(5, 300, "Arm1", "DIW", 1, ProcessArmMode.Scan, 150, 30, 101)), ErrorCodes.ProcessRecipeScanSpeedOutOfRange, ["2", "0.1", "100"], "Scan 速度超过上限");
    Fails(library.Save(1, 2, string.Empty, [Step(600, 300), Step(1.5, 300)], "Saver"), ErrorCodes.ProcessRecipeTotalTooLong, ["601.5", "600"], "合计超过腔体工艺超时");
    Fails(library.Save(7, 1, string.Empty, good, "Saver"), ErrorCodes.ProcessRecipeNotFound, ["7"], "没有的编号");
    Check(library.Get(1).Recipe?.Revision == 2 && changed.Count == 2, "没存成的不改库、不发事件");
    Check(SaveOne(Step(5, 0, "Arm3", "sc1", 0.1, ProcessArmMode.Scan, 0, 150, 0.1)).IsOk, "边界值都能存：转速 0、流量 0.1、位置 0 和 150、速度 0.1");

    // 4. 改名：版本加 1、记修改人；跟别的编号重了不行。
    Check(library.Create(2, "DIW_RINSE", "Tester").IsOk, "再建一个");
    Fails(library.Rename(2, "SC1_60S", "Tester"), ErrorCodes.ProcessRecipeNameDuplicate, ["SC1_60S", "1"], "改名跟别的编号重了");
    Fails(library.Rename(9, "NOBODY", "Tester"), ErrorCodes.ProcessRecipeNotFound, ["9"], "改没有的编号");
    var renamed = library.Rename(1, "SC1_45S", "Renamer");
    Check(renamed.IsOk && renamed.Recipe is not null && renamed.Recipe.Name == "SC1_45S" && renamed.Recipe.Revision == 4
          && renamed.Recipe.ModifiedBy == "Renamer" && renamed.Recipe.Steps.Count == 2, "改名：版本加 1，步骤不动");

    // 5. 名字查库（流程配方、腔体起工艺用）：不分大小写、去掉首尾空白。
    Check(library.Contains(" sc1_45s ") && !library.Contains("SC1_60S") && !library.Contains(string.Empty), "按名字查库");

    // 6. 文件读回来：新开一个库读同一个目录，内容、版本一样；用不上的字段没写进文件；读不出来的、文件名不是编号的、超出个数的都跳过。
    File.WriteAllText(Path.Combine(folder, "005.xml"), "<ProcessRecipe Name=");
    File.WriteAllText(Path.Combine(folder, "notes.xml"), "<ProcessRecipe Name=\"NOTES\" />");
    File.WriteAllText(Path.Combine(folder, "120.xml"), "<ProcessRecipe Name=\"TOO_BIG\" />");
    var reopened = new ProcessRecipeComponent { Folder = folder };
    reopened.Load();
    ProcessRecipeComponent.Current = library;
    var reloaded = reopened.List();
    Check(reloaded.Select(item => item.Index).SequenceEqual(new[] { 1, 2 }), "坏文件、不是编号的、超出个数的都跳过");
    var back = reloaded[0];
    Check(back.Name == "SC1_45S" && back.Revision == 4 && back.CreatedBy == "Tester" && back.ModifiedBy == "Renamer"
          && back.Steps.Count == 2 && back.Steps[0].Arm == "Arm1" && back.Steps[0].Chemical == "DIW"
          && back.Steps[1].Arm == "Arm3" && back.Steps[1].Mode == ProcessArmMode.Scan && back.Steps[1].Position == 0
          && back.Steps[1].ScanTo == 150 && back.Steps[1].ScanSpeed == 0.1 && back.Steps[1].Flow == 0.1, "读回来跟存的一样");
    Check(!Directory.EnumerateFiles(folder, "*.tmp").Any(), "写完不留临时文件");

    // 7. 给出去的都是副本：改了不影响库里的。
    var copy = library.Get(1).Recipe!;
    copy.Name = "HACKED";
    copy.Steps.Clear();
    Check(library.Get(1).Recipe?.Name == "SC1_45S" && library.Get(1).Recipe?.Steps.Count == 2, "拿到的是副本");

    // 8. 删除：文件删掉、编号空出来；再删、再取都说没有；没成的不发事件。
    changed.Clear();
    Check(library.Delete(2, "Tester").IsOk && !File.Exists(Path.Combine(folder, "002.xml")) && library.List().Count == 1, "删除");
    Fails(library.Delete(2, "Tester"), ErrorCodes.ProcessRecipeNotFound, ["2"], "再删说没有");
    Fails(library.Get(2), ErrorCodes.ProcessRecipeNotFound, ["2"], "再取说没有");
    Fails(library.Get(0), ErrorCodes.ProcessRecipeIndexOutOfRange, ["0", "99"], "取编号 0");
    Check(changed.SequenceEqual(new[] { 2 }), "删除发一次变更事件");

    // 9. 工艺配方服务（直接调 gRPC 服务类，不起网络）：列表带个数、说明、合计时长；选项带摆臂、药液和范围；
    //    错误码和参数原样回给界面；没带操作人记成 Unknown；没装库回 process_recipe.not_installed。
    var service = new ProcessRecipeService([]);
    var listDto = (await service.GetListAsync(new RpcRequest())).DeserializeData<ProcessRecipeListDto>();
    Check(listDto.Capacity == 99 && listDto.Items.Count == 1 && listDto.Items[0].Index == 1 && listDto.Items[0].Name == "SC1_45S"
          && listDto.Items[0].TotalSeconds == 10, "列表：个数 99，编号、名称、合计时长");
    var options = (await service.GetOptionsAsync(new RpcRequest())).DeserializeData<ProcessRecipeOptionsDto>();
    Check(options.Arms.Count == 2 && options.Arms[0].Name == "Arm1" && options.Arms[0].Chemicals.SequenceEqual(new[] { "DIW", "SC1", "HF" }),
        "选项：摆臂和药液照库里的");
    Check(options.MinSeconds == 0.1 && options.MaxSeconds == 3600 && options.MaxRpm == 3000 && options.MinFlow == 0.1 && options.MaxFlow == 3
          && options.MinPosition == 0 && options.MaxPosition == 150 && options.MinScanSpeed == 0.1 && options.MaxScanSpeed == 100
          && options.MaxTotalSeconds == 600, "选项：各项范围和合计上限");
    Check(options.NewStepSeconds == ProcessRecipeComponent.NewStepSeconds && options.NewStepRpm == ProcessRecipeComponent.NewStepRpm
          && created.Recipe.Steps[0].Seconds == options.NewStepSeconds && created.Recipe.Steps[0].Rpm == options.NewStepRpm,
        "选项：新加一步的默认值跟新建时的第一步一样");
    var one = (await service.GetAsync(new ProcessRecipeIndexRequest { Index = 1 })).DeserializeData<ProcessRecipeDto>();
    Check(one.Index == 1 && one.Revision == 4 && one.Steps.Count == 2 && one.Steps[1].Mode == ProcessArmMode.Scan && one.Steps[1].ScanTo == 150,
        "取一个：头信息和步骤（方式、Scan 的另一头）");

    var createdBySvc = await service.CreateAsync(new ProcessRecipeCreateRequest { Index = 3, Name = "SVC_NEW" });
    var createdDto = createdBySvc.DeserializeData<ProcessRecipeDto>();
    Check(createdBySvc.Success && createdDto.Index == 3 && createdDto.CreatedBy == "Unknown" && createdDto.Steps.Count == 1,
        "服务新建：没带操作人记成 Unknown");
    List<ProcessRecipeStepDto> ServiceSteps(double flow)
    {
        return
        [
            new ProcessRecipeStepDto { Seconds = 30, Rpm = 800, Arm = "Arm1", Chemical = "DIW", Flow = flow, Mode = ProcessArmMode.Scan, Position = 150, ScanTo = 60, ScanSpeed = 30 },
            new ProcessRecipeStepDto { Seconds = 20, Rpm = 2000 },
        ];
    }

    var savedBySvc = await service.SaveAsync(new ProcessRecipeSaveRequest { Index = 3, Revision = 1, Description = "d", Operator = "Op", Steps = ServiceSteps(1.5) });
    var savedDto = savedBySvc.DeserializeData<ProcessRecipeDto>();
    Check(savedBySvc.Success && savedDto.Revision == 2 && savedDto.ModifiedBy == "Op" && savedDto.Steps[0].ScanTo == 60, "服务保存：版本加 1");
    var stale = await service.SaveAsync(new ProcessRecipeSaveRequest { Index = 3, Revision = 1, Operator = "Op", Steps = ServiceSteps(1.5) });
    Check(!stale.Success && stale.Code == ErrorCodes.ProcessRecipeRevisionMismatch && stale.Args.SequenceEqual(new[] { "3" }), "版本冲突：错误码带编号");
    var badFlow = await service.SaveAsync(new ProcessRecipeSaveRequest { Index = 3, Revision = 2, Operator = "Op", Steps = ServiceSteps(9) });
    Check(!badFlow.Success && badFlow.Code == ErrorCodes.ProcessRecipeFlowOutOfRange && badFlow.Args.SequenceEqual(new[] { "1", "0.1", "3" }),
        "流量超了：错误码带步号和范围");
    var duplicate = await service.RenameAsync(new ProcessRecipeRenameRequest { Index = 3, Name = "sc1_45s", Operator = "Op" });
    Check(!duplicate.Success && duplicate.Code == ErrorCodes.ProcessRecipeNameDuplicate && duplicate.Args.SequenceEqual(new[] { "sc1_45s", "1" }),
        "改名重了：错误码带名称和编号");

    // 10. 腔体起工艺：配方要在库里；在库里的照常往下走（探针腔体不接动作，回动作被拒）。
    var chamberService = new ChamberService([pm1]);
    var missing = await chamberService.ProcessAsync(new ChamberProcessRequest { Module = "SmokePM1", Recipe = " NOPE " });
    Check(!missing.Success && missing.Code == ErrorCodes.ChamberRecipeNotFound && missing.Args.SequenceEqual(new[] { "SmokePM1", "NOPE" }),
        "腔体起工艺：配方不在库里，错误码带模块和配方名");
    var known = await chamberService.ProcessAsync(new ChamberProcessRequest { Module = "SmokePM1", Recipe = "svc_new" });
    Check(known.Code != ErrorCodes.ChamberRecipeNotFound && known.Code != ErrorCodes.RecipeRequired, "在库里的配方（不分大小写）不挡");
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
    Check(notChecked.Code != ErrorCodes.ChamberRecipeNotFound, "没装库时腔体起工艺不查配方在不在库里");
}
finally
{
    ProcessRecipeComponent.Current = null;
    if (Directory.Exists(folder))
    {
        Directory.Delete(folder, recursive: true);
    }
}

Console.WriteLine($"PASS: {checks} process recipe checks (sc.xml node, arms and chemicals from chamber arms and nozzles, create/rename/save/delete with every rule, normalizing and unused fields left out of the file, files read back with bad files skipped, revision conflicts, change events, the process recipe service, and chamber process requiring a recipe in the library).");

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
