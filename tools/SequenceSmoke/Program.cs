using System.Diagnostics.CodeAnalysis;
using xyz.Components;
using xyz.Components.Components;
using xyz.Configs.Models;
using xyz.Modules;
using xyz.Modules.Enums;
using xyz.Service.Recipes;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;
using xyz.Shared.Rpc;
using xyz.Tools;

// 流程配方冒烟：sc.xml 节点、可选站点分组（按 sc 的分组节点 + 机械手站点表生成）、新建 / 改名 / 保存 / 删除和各项检查、
// 文件读写（一个编号一个文件、坏文件跳过）、版本冲突、变更事件、gRPC 服务的错误码。不连设备，文件写在临时目录，跑完删掉。
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException("FAIL: " + message);
    }

    checks++;
}

void Fails(SequenceResult result, string code, string[] args, string message)
{
    Check(!result.IsOk && result.Code == code && result.Args.SequenceEqual(args),
        $"{message}，实际 {result.Code} [{string.Join(",", result.Args)}]");
}

ModuleConfig Module(string name)
{
    return new ModuleConfig { Name = name, Type = "Probe" };
}

ModuleConfig Group(string name, params ModuleConfig[] children)
{
    return new ModuleConfig { Name = name, Children = children.ToList() };
}

string folder = Path.Combine(Path.GetTempPath(), "xyz-sequence-smoke-" + Guid.NewGuid().ToString("N"));
try
{
    // 0. 真 sc.xml 里配了流程配方库节点，三个参数都写全；装配时 Folder 换成临时目录（不往运行目录写文件）。
    var scConfig = XmlHelper.Deserialize<ScConfig>(Path.Combine(AppContext.BaseDirectory, "Config", "sc.xml"));
    Check(scConfig is not null, "sc.xml 解析失败");
    var node = scConfig!.Modules.FirstOrDefault(setting => string.Equals(setting.Name, "Sequence", StringComparison.OrdinalIgnoreCase));
    Check(node is not null && node.Type == typeof(SequenceComponent).FullName, "sc.xml 里应有指向 SequenceComponent 的 Sequence 节点");
    string ValueOf(string name)
    {
        return node!.Values.First(value => string.Equals(value.Name, name, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;
    }

    Check(ValueOf("Capacity") == "99" && ValueOf("NameMaxLength") == "32" && ValueOf("Folder") == "Recipe\\Sequence",
        "sc.xml 里写全三个参数：个数 99、名称最长 32、目录 Recipe\\Sequence");
    node!.Values.First(value => string.Equals(value.Name, "Folder", StringComparison.OrdinalIgnoreCase)).Value = folder;

    var roots = ComponentLoader.Load([node]);
    var library = roots.OfType<SequenceComponent>().Single();
    Check(library.Capacity == 99 && library.NameMaxLength == 32, "装出一个流程配方库，参数按 sc.xml");
    Check(ReferenceEquals(SequenceComponent.Current, library), "装配出来即成为 Current");
    Check(Directory.Exists(folder) && library.List().Count == 0, "目录没有就建，开始是空的");

    // 1. 可选站点分组：按 sc.xml 的分组节点和下面装的模块，只留能放片、机械手站点表里有的；顶层直接装的站点自成一组；
    //    Robot 分组、不是模块的节点（Database）、机械手到不了的站点（SmokePM3）都不算。
    var lp1 = new ProbePort("SmokeLP1");
    var lp2 = new ProbePort("SmokeLP2");
    var pm1 = new ProbeChamber("SmokePM1");
    var pm2 = new ProbeChamber("SmokePM2");
    var pm3 = new ProbeChamber("SmokePM3");
    var aligner = new ProbeStation("SmokeAligner");
    var robot = new ProbeRobot("SmokeRobot", "SmokeLP1", "SmokeLP2", "SmokePM1", "SmokePM2", "SmokeAligner");
    var settings = new List<ModuleConfig>
    {
        Group("Database", Module("Default")),
        Group("LoadPort", Module("SmokeLP1"), Module("SmokeLP2")),
        Group("Robot", Module("SmokeRobot")),
        Group("Chamber", Module("SmokePM1"), Module("SmokePM2"), Module("SmokePM3")),
        Module("SmokeAligner"),
    };
    library.Bind(settings, [lp1, lp2, robot, pm1, pm2, pm3, aligner]);
    var groups = library.StationGroups;
    Check(groups.Select(group => group.Name).SequenceEqual(new[] { "LoadPort", "Chamber", "SmokeAligner" }),
        "分组按 sc.xml 的先后：LoadPort、Chamber、顶层的 SmokeAligner，实际 " + string.Join(",", groups.Select(group => group.Name)));
    Check(groups[0].Modules.SequenceEqual(new[] { "SmokeLP1", "SmokeLP2" }) && groups[0].IsLoadPort && !groups[0].NeedsRecipe,
        "LoadPort 分组：两个 LoadPort，不要工艺配方");
    Check(groups[1].Modules.SequenceEqual(new[] { "SmokePM1", "SmokePM2" }) && !groups[1].IsLoadPort && groups[1].NeedsRecipe,
        "Chamber 分组：机械手到不了的 SmokePM3 不列，要工艺配方");
    Check(groups[2].Modules.SequenceEqual(new[] { "SmokeAligner" }) && !groups[2].IsLoadPort && !groups[2].NeedsRecipe,
        "顶层直接装的站点自成一组");

    // 2. 新建：空编号上建，名称去掉首尾空白；内容按默认（LoadPort 全勾 → 第一个要工艺配方的分组全勾、配方留空 → LoadPort 全勾），
    //    版本 1，记创建人；写成 001.xml。名称、编号不对的回错误码，不建、不发事件。
    var changed = new List<int>();
    library.Changed += index => changed.Add(index);
    var created = library.Create(1, "  SC1_CLEAN ", "Tester");
    Check(created.IsOk && created.Sequence is not null && created.Sequence.Name == "SC1_CLEAN" && created.Sequence.Revision == 1
          && created.Sequence.CreatedBy == "Tester" && created.Sequence.ModifiedBy == "Tester", "新建：版本 1，记创建人");
    var defaults = created.Sequence!.Steps;
    Check(defaults.Count == 3
          && defaults[0].Group == "LoadPort" && defaults[0].Stations.SequenceEqual(new[] { "SmokeLP1", "SmokeLP2" })
          && defaults[1].Group == "Chamber" && defaults[1].Stations.SequenceEqual(new[] { "SmokePM1", "SmokePM2" }) && defaults[1].Recipe == string.Empty
          && defaults[2].Group == "LoadPort" && defaults[2].Stations.SequenceEqual(new[] { "SmokeLP1", "SmokeLP2" }),
        "默认步骤：LoadPort → Chamber → LoadPort，全勾");
    Check(File.Exists(Path.Combine(folder, "001.xml")), "一个编号一个文件：001.xml");
    Check(changed.SequenceEqual(new[] { 1 }), "新建发一次变更事件");

    Fails(library.Create(1, "OTHER", "Tester"), ErrorCodes.SequenceIndexOccupied, ["1", "SC1_CLEAN"], "编号已经有了");
    Fails(library.Create(2, "sc1_clean", "Tester"), ErrorCodes.SequenceNameDuplicate, ["sc1_clean", "1"], "名称重了（不分大小写）");
    Fails(library.Create(2, "SC1 CLEAN", "Tester"), ErrorCodes.SequenceNameInvalid, ["SC1 CLEAN"], "名称里有空格");
    Fails(library.Create(2, "流程一", "Tester"), ErrorCodes.SequenceNameInvalid, ["流程一"], "名称只能用字母、数字、_ 和 -");
    Fails(library.Create(2, "   ", "Tester"), ErrorCodes.SequenceNameRequired, [], "名称没填");
    Fails(library.Create(2, new string('A', 33), "Tester"), ErrorCodes.SequenceNameTooLong, ["32"], "名称超过 32 个字符");
    Fails(library.Create(0, "ZERO", "Tester"), ErrorCodes.SequenceIndexOutOfRange, ["0", "99"], "编号 0");
    Fails(library.Create(100, "HUNDRED", "Tester"), ErrorCodes.SequenceIndexOutOfRange, ["100", "99"], "编号超过个数");
    Check(changed.Count == 1 && library.List().Count == 1, "没成的不建、不发事件");

    // 3. 保存：版本对得上、步骤检查通过才存，版本加 1、记修改人；分组、站点按 sc 的写法和先后规整，不要配方的分组把配方清掉。
    var good = new List<SequenceStep>
    {
        new("loadport", ["smokelp2", "SmokeLP1"], "IGNORED"),
        new("Chamber", ["SmokePM2"], " SC1_60S "),
        new("LoadPort", ["SmokeLP1", "SmokeLP2"]),
    };
    var saved = library.Save(1, 1, " 药洗 ", good, "Saver");
    Check(saved.IsOk && saved.Sequence is not null && saved.Sequence.Revision == 2 && saved.Sequence.ModifiedBy == "Saver"
          && saved.Sequence.CreatedBy == "Tester" && saved.Sequence.Description == "药洗", "保存：版本 2，记修改人，说明去掉首尾空白");
    var savedSteps = saved.Sequence!.Steps;
    Check(savedSteps[0].Group == "LoadPort" && savedSteps[0].Stations.SequenceEqual(new[] { "SmokeLP1", "SmokeLP2" }) && savedSteps[0].Recipe == string.Empty
          && savedSteps[1].Stations.SequenceEqual(new[] { "SmokePM2" }) && savedSteps[1].Recipe == "SC1_60S",
        "规整：分组、站点按 sc 的写法和先后，LoadPort 不带配方，配方去掉空白");
    Check(changed.SequenceEqual(new[] { 1, 1 }), "保存发一次变更事件");

    Fails(library.Save(1, 1, string.Empty, good, "Saver"), ErrorCodes.SequenceRevisionMismatch, ["1"], "旧版本存不进去（别处改过）");
    Fails(library.Save(1, 2, string.Empty, good.Take(2).ToList(), "Saver"), ErrorCodes.SequenceTooFewSteps, [], "少于 3 步");
    Fails(library.Save(1, 2, string.Empty, [new("Chamber", ["SmokePM1"], "R"), good[1], good[2]], "Saver"),
        ErrorCodes.SequenceStepNotLoadPort, ["1"], "第 1 步不是 LoadPort");
    Fails(library.Save(1, 2, string.Empty, [good[0], good[1], new("Chamber", ["SmokePM1"], "R")], "Saver"),
        ErrorCodes.SequenceStepNotLoadPort, ["3"], "最后一步不是 LoadPort");
    Fails(library.Save(1, 2, string.Empty, [good[0], new("Robot", ["SmokeRobot"]), good[2]], "Saver"),
        ErrorCodes.SequenceGroupNotFound, ["2", "Robot"], "Robot 不是可选分组");
    Fails(library.Save(1, 2, string.Empty, [good[0], new("Chamber", [], "R"), good[2]], "Saver"),
        ErrorCodes.SequenceStationRequired, ["2"], "一个站点都没勾");
    Fails(library.Save(1, 2, string.Empty, [good[0], new("Chamber", ["SmokeLP1"], "R"), good[2]], "Saver"),
        ErrorCodes.SequenceStationNotInGroup, ["2", "SmokeLP1", "Chamber"], "站点不在这一步的分组里");
    Fails(library.Save(1, 2, string.Empty, [good[0], new("Chamber", ["SmokePM3"], "R"), good[2]], "Saver"),
        ErrorCodes.SequenceStationNotInGroup, ["2", "SmokePM3", "Chamber"], "机械手到不了的站点不能用");
    Fails(library.Save(1, 2, string.Empty, [good[0], new("Chamber", ["SmokePM1"], "  "), good[2]], "Saver"),
        ErrorCodes.SequenceRecipeRequired, ["2"], "工艺腔那一步要选工艺配方");
    Fails(library.Save(7, 1, string.Empty, good, "Saver"), ErrorCodes.SequenceNotFound, ["7"], "没有的编号");
    Check(library.Get(1).Sequence?.Revision == 2 && changed.Count == 2, "没存成的不改库、不发事件");

    var lp1ToLp2 = library.Save(1, 2, string.Empty,
        [new("LoadPort", ["SmokeLP1"]), new("Chamber", ["SmokePM1"], "R1"), new("SmokeAligner", ["SmokeAligner"]), new("LoadPort", ["SmokeLP2"])],
        "Saver");
    Check(lp1ToLp2.IsOk && lp1ToLp2.Sequence is not null && lp1ToLp2.Sequence.Revision == 3 && lp1ToLp2.Sequence.Steps.Count == 4,
        "LoadPort1 取、放到 LoadPort2，中间还能经过别的站点分组");

    // 4. 改名：版本加 1、记修改人；跟别的编号重了不行。
    Check(library.Create(2, "DIW_RINSE", "Tester").IsOk, "再建一个");
    Fails(library.Rename(2, "SC1_CLEAN", "Tester"), ErrorCodes.SequenceNameDuplicate, ["SC1_CLEAN", "1"], "改名跟别的编号重了");
    Fails(library.Rename(9, "NOBODY", "Tester"), ErrorCodes.SequenceNotFound, ["9"], "改没有的编号");
    var renamed = library.Rename(1, "SC1_LP1_TO_LP2", "Renamer");
    Check(renamed.IsOk && renamed.Sequence is not null && renamed.Sequence.Name == "SC1_LP1_TO_LP2" && renamed.Sequence.Revision == 4
          && renamed.Sequence.ModifiedBy == "Renamer" && renamed.Sequence.Steps.Count == 4, "改名：版本加 1，步骤不动");

    // 5. 文件读回来：新开一个库读同一个目录，内容、版本一样；读不出来的、文件名不是编号的、超出个数的都跳过，不连累别的。
    File.WriteAllText(Path.Combine(folder, "005.xml"), "<Sequence Name=");
    File.WriteAllText(Path.Combine(folder, "notes.xml"), "<Sequence Name=\"NOTES\" />");
    File.WriteAllText(Path.Combine(folder, "120.xml"), "<Sequence Name=\"TOO_BIG\" />");
    var reopened = new SequenceComponent { Folder = folder };
    reopened.Load();
    SequenceComponent.Current = library;
    var reloaded = reopened.List();
    Check(reloaded.Select(item => item.Index).SequenceEqual(new[] { 1, 2 }), "坏文件、不是编号的、超出个数的都跳过");
    var back = reloaded[0];
    Check(back.Name == "SC1_LP1_TO_LP2" && back.Revision == 4 && back.CreatedBy == "Tester" && back.ModifiedBy == "Renamer"
          && back.Steps.Count == 4 && back.Steps[0].Stations.SequenceEqual(new[] { "SmokeLP1" })
          && back.Steps[1].Recipe == "R1" && back.Steps[3].Stations.SequenceEqual(new[] { "SmokeLP2" }), "读回来跟存的一样");
    Check(!Directory.EnumerateFiles(folder, "*.tmp").Any(), "写完不留临时文件");

    // 6. 给出去的都是副本：改了不影响库里的。
    var copy = library.Get(1).Sequence!;
    copy.Name = "HACKED";
    copy.Steps.Clear();
    Check(library.Get(1).Sequence?.Name == "SC1_LP1_TO_LP2" && library.Get(1).Sequence?.Steps.Count == 4, "拿到的是副本");

    // 7. 删除：文件删掉、编号空出来；再删、再取都说没有；没成的不发事件。
    changed.Clear();
    Check(library.Delete(2, "Tester").IsOk && !File.Exists(Path.Combine(folder, "002.xml")) && library.List().Count == 1, "删除");
    Fails(library.Delete(2, "Tester"), ErrorCodes.SequenceNotFound, ["2"], "再删说没有");
    Fails(library.Get(2), ErrorCodes.SequenceNotFound, ["2"], "再取说没有");
    Fails(library.Get(0), ErrorCodes.SequenceIndexOutOfRange, ["0", "99"], "取编号 0");
    Check(changed.SequenceEqual(new[] { 2 }), "删除发一次变更事件");

    // 8. 流程配方服务（直接调 gRPC 服务类，不起网络）：列表带个数，分组照库里的，错误码和参数原样回给界面；
    //    没带操作人记成 Unknown；没装库回 sequence.not_installed。
    var service = new SequenceService([]);
    var listDto = (await service.GetListAsync(new RpcRequest())).DeserializeData<SequenceListDto>();
    Check(listDto.Capacity == 99 && listDto.Items.Count == 1 && listDto.Items[0].Index == 1 && listDto.Items[0].Name == "SC1_LP1_TO_LP2",
        "列表：个数 99，用了的编号和名称");
    var groupDtos = (await service.GetStationGroupsAsync(new RpcRequest())).DeserializeData<List<SequenceStationGroupDto>>();
    Check(groupDtos.Count == 3 && groupDtos[0].IsLoadPort && groupDtos[1].Name == "Chamber" && groupDtos[1].NeedsRecipe
          && groupDtos[1].Modules.SequenceEqual(new[] { "SmokePM1", "SmokePM2" }), "分组");
    var one = (await service.GetAsync(new SequenceIndexRequest { Index = 1 })).DeserializeData<SequenceDto>();
    Check(one.Index == 1 && one.Revision == 4 && one.Steps.Count == 4 && one.Steps[3].Stations.SequenceEqual(new[] { "SmokeLP2" }),
        "取一个：头信息和步骤");

    var createdBySvc = await service.CreateAsync(new SequenceCreateRequest { Index = 3, Name = "SVC_NEW" });
    var createdDto = createdBySvc.DeserializeData<SequenceDto>();
    Check(createdBySvc.Success && createdDto.Index == 3 && createdDto.CreatedBy == "Unknown" && createdDto.Steps.Count == 3,
        "服务新建：没带操作人记成 Unknown");
    List<SequenceStepDto> ServiceSteps()
    {
        return
        [
            new SequenceStepDto { Group = "LoadPort", Stations = ["SmokeLP1"] },
            new SequenceStepDto { Group = "Chamber", Stations = ["SmokePM1"], Recipe = "R" },
            new SequenceStepDto { Group = "LoadPort", Stations = ["SmokeLP1"] },
        ];
    }

    var savedBySvc = await service.SaveAsync(new SequenceSaveRequest { Index = 3, Revision = 1, Description = "d", Operator = "Op", Steps = ServiceSteps() });
    Check(savedBySvc.Success && savedBySvc.DeserializeData<SequenceDto>().Revision == 2, "服务保存：版本加 1");
    var stale = await service.SaveAsync(new SequenceSaveRequest { Index = 3, Revision = 1, Operator = "Op", Steps = ServiceSteps() });
    Check(!stale.Success && stale.Code == ErrorCodes.SequenceRevisionMismatch && stale.Args.SequenceEqual(new[] { "3" }), "版本冲突：错误码带编号");
    var noRecipe = await service.SaveAsync(new SequenceSaveRequest
    {
        Index = 3,
        Revision = 2,
        Operator = "Op",
        Steps = [new SequenceStepDto { Group = "LoadPort", Stations = ["SmokeLP1"] }, new SequenceStepDto { Group = "Chamber", Stations = ["SmokePM1"] }, new SequenceStepDto { Group = "LoadPort", Stations = ["SmokeLP1"] }],
    });
    Check(!noRecipe.Success && noRecipe.Code == ErrorCodes.SequenceRecipeRequired && noRecipe.Args.SequenceEqual(new[] { "2" }), "没选工艺配方：错误码带步号");
    var duplicate = await service.RenameAsync(new SequenceRenameRequest { Index = 3, Name = "sc1_lp1_to_lp2", Operator = "Op" });
    Check(!duplicate.Success && duplicate.Code == ErrorCodes.SequenceNameDuplicate && duplicate.Args.SequenceEqual(new[] { "sc1_lp1_to_lp2", "1" }),
        "改名重了：错误码带名称和编号");
    Check((await service.DeleteAsync(new SequenceDeleteRequest { Index = 3, Operator = "Op" })).Success, "服务删除");
    var gone = await service.GetAsync(new SequenceIndexRequest { Index = 3 });
    Check(!gone.Success && gone.Code == ErrorCodes.SequenceNotFound && gone.Args.SequenceEqual(new[] { "3" }), "删了再取：没有");

    SequenceComponent.Current = null;
    var notInstalled = await service.GetListAsync(new RpcRequest());
    Check(!notInstalled.Success && notInstalled.Code == ErrorCodes.SequenceNotInstalled && notInstalled.Args.Count == 0, "没装库：sequence.not_installed");

    // 9. 装了工艺配方库（按字段表装：时间、摆臂、药液跟着摆臂走）：工艺步骤选的配方要在库里（不分大小写），不在的存不进去，
    //    错误码带步号和配方名；配方里从腔体部件取的下拉，勾的每个腔体都要有（SmokePM1 的 Arm1 只有 DIW，SmokePM2 的还有 HF），
    //    没有的存不进去，错误码带步号、腔体、配方、字段和值（上面几节没装库，不查）。
    string recipeFolder = Path.Combine(Path.GetTempPath(), "xyz-sequence-smoke-recipes-" + Guid.NewGuid().ToString("N"));
    try
    {
        ModuleConfig FieldNode(string key, params (string Name, string Value)[] values)
        {
            return new ModuleConfig { Name = key, Values = values.Select(value => new ValueConfig { Name = value.Name, Value = value.Value }).ToList() };
        }

        var recipeNode = new ModuleConfig
        {
            Name = "ProcessRecipe",
            Type = typeof(ProcessRecipeComponent).FullName,
            Values = [new ValueConfig { Name = "Folder", Value = recipeFolder }],
            Children =
            [
                new ModuleConfig
                {
                    Name = ProcessRecipeComponent.FieldsNodeName,
                    Children =
                    [
                        FieldNode("Seconds", ("Text", "时间"), ("Type", "Double"), ("Min", "0.1"), ("Default", "10"), ("Required", "true")),
                        FieldNode("Arm", ("Text", "摆臂"), ("Type", "Choice"), ("Source", "Parts:ArmAxisComponent")),
                        FieldNode("Chemical", ("Text", "药液"), ("Type", "Choice"), ("Source", "Parts:NozzleComponent.Chemical@Arm")),
                    ],
                },
            ],
        };
        var recipes = (ProcessRecipeComponent)ComponentLoader.Load([recipeNode]).Single();
        void Mount(ProbeChamber chamber, params string[] chemicals)
        {
            var arm = new ArmAxisComponent();
            Probe.Name(arm, "Arm1");
            chamber.AddChild(arm);
            foreach (string chemical in chemicals)
            {
                var nozzle = new NozzleComponent { Chemical = chemical };
                Probe.Name(nozzle, "Nozzle_" + chemical);
                arm.AddChild(nozzle);
            }
        }

        Mount(pm1, "DIW");
        Mount(pm2, "DIW", "HF");
        recipes.Bind([pm1, pm2, pm3]);
        ProcessRecipeStep RecipeStep(string chemical)
        {
            return new ProcessRecipeStep { Values = [new("Seconds", "10"), new("Arm", "Arm1"), new("Chemical", chemical)] };
        }

        Check(ReferenceEquals(ProcessRecipeComponent.Current, recipes) && recipes.Create(1, "SC1_60S", "Tester").IsOk
              && recipes.Save(1, 1, string.Empty, [RecipeStep("DIW")], "Tester").IsOk, "工艺配方库里建一个 SC1_60S（用 DIW）");
        Check(recipes.Create(2, "HF_ONLY", "Tester").IsOk && recipes.Save(2, 1, string.Empty, [RecipeStep("HF")], "Tester").IsOk,
            "再建一个 HF_ONLY（用 HF，只有 SmokePM2 有）");
        int revision = library.Get(1).Sequence!.Revision;
        Fails(library.Save(1, revision, string.Empty, [new("LoadPort", ["SmokeLP1"]), new("Chamber", ["SmokePM1"], " NOPE "), new("LoadPort", ["SmokeLP1"])], "Saver"),
            ErrorCodes.SequenceRecipeNotFound, ["2", "NOPE"], "工艺配方不在库里存不进去");
        var withRecipe = library.Save(1, revision, string.Empty, [new("LoadPort", ["SmokeLP1"]), new("Chamber", ["SmokePM1"], "sc1_60s"), new("LoadPort", ["SmokeLP1"])], "Saver");
        Check(withRecipe.IsOk && withRecipe.Sequence?.Steps[1].Recipe == "sc1_60s", "库里有的（不分大小写）存得进去");
        revision = withRecipe.Sequence!.Revision;
        Fails(library.Save(1, revision, string.Empty, [new("LoadPort", ["SmokeLP1"]), new("Chamber", ["SmokePM1", "SmokePM2"], "hf_only"), new("LoadPort", ["SmokeLP1"])], "Saver"),
            ErrorCodes.SequenceRecipeOptionMissing, ["2", "SmokePM1", "hf_only", "药液", "HF"], "勾的 SmokePM1 没有 HF：存不进去");
        Check(library.Save(1, revision, string.Empty, [new("LoadPort", ["SmokeLP1"]), new("Chamber", ["SmokePM2"], "HF_ONLY"), new("LoadPort", ["SmokeLP1"])], "Saver").IsOk,
            "只勾有 HF 的 SmokePM2：存得进去");
    }
    finally
    {
        ProcessRecipeComponent.Current = null;
        if (Directory.Exists(recipeFolder))
        {
            Directory.Delete(recipeFolder, recursive: true);
        }
    }
}
finally
{
    SequenceComponent.Current = null;
    if (Directory.Exists(folder))
    {
        Directory.Delete(folder, recursive: true);
    }
}

Console.WriteLine($"PASS: {checks} sequence checks (sc.xml node, station groups from sc groups and robot stations, create/rename/save/delete with every rule, files written and read back with bad files skipped, revision conflicts, change events, the sequence service error codes, and recipes required to be in the process recipe library and to fit every chamber picked).");

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

// 探针 LoadPort：只给站点分组认类型、认名字，不连设备、不做动作。
sealed class ProbePort : BaseLoadPortModule
{
    public ProbePort(string name)
    {
        Probe.Name(this, name);
    }

    public override ModuleOperation? Load() => null;

    public override ModuleOperation? Unload() => null;

    public override ModuleOperation? Home() => null;

    protected override ModuleOperation? ResetDevice() => null;

    protected override ModuleOperation? AbortDevice() => null;

    public override ModuleOperation? Clamp() => null;

    public override ModuleOperation? Unclamp() => null;
}

// 探针腔体：要工艺配方的那一类站点。
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

// 探针普通站点（Aligner 这类）：能放片，不要工艺配方。
sealed class ProbeStation : BaseTransferStationModule
{
    public ProbeStation(string name)
    {
        Probe.Name(this, name);
    }

    public override int State { get; protected set; } = ModuleState.Idle;

    public override int SlotCount { get; set; } = 1;
}

// 探针机械手：只给一张站点表（机械手到得了的站点），不连设备、不做动作。
sealed class ProbeRobot : BaseModule, IRobot
{
    private readonly Dictionary<string, RobotStation> _stations = new(StringComparer.OrdinalIgnoreCase);

    public ProbeRobot(string name, params string[] stations)
    {
        Probe.Name(this, name);
        for (int index = 0; index < stations.Length; index++)
        {
            _stations[stations[index]] = new RobotStation(stations[index], index + 1, RobotDirection.South, 0, [1, 2]);
        }
    }

    public override int State { get; protected set; } = ModuleState.Idle;

    public bool? IsServoOn => null;

    public string? DeviceError => null;

    public bool? HasWafer(int arm) => null;

    public IReadOnlyDictionary<string, RobotStation> Stations => _stations;

    public bool TryGetStation(string station, [MaybeNullWhen(false)] out RobotStation config) => _stations.TryGetValue(station, out config);

    ModuleOperation? IRobot.Home() => null;

    ModuleOperation? IRobot.Init() => null;

    ModuleOperation? IRobot.Reset() => null;

    ModuleOperation? IRobot.Abort() => null;

    ModuleOperation? IRobot.Pick(int arm, string station, int slot) => null;

    ModuleOperation? IRobot.Place(int arm, string station, int slot) => null;

    ModuleOperation? IRobot.PowerOn() => null;

    ModuleOperation? IRobot.PowerOff() => null;
}
