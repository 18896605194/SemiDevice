using System.Xml.Linq;
using ToolBuilder.Core;

var workspace = Path.Combine(Path.GetTempPath(), "ToolBuilderSmoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workspace);
var passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("失败：" + description);
    passed++;
    Console.WriteLine("PASS " + description);
}
try
{
    var path = Path.Combine(workspace, "machine.xml");
    var original = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<ArrayOfSetting Custom=\"保留\">\r\n  <!-- 原有注释 -->\r\n  <Setting Name=\"Unknown\" Type=\"future.Component\" Extra=\"保留\">\r\n    <Value Name=\"Chinese\" Value=\"测试中文\" />\r\n    <VendorExtension Mode=\"keep\" />\r\n  </Setting>\r\n</ArrayOfSetting>";
    File.WriteAllText(path, original);
    var document = ConfigDocument.Open(path);
    Check(!document.IsDirty, "打开文件不产生修改");
    Check(document.Xml.DescendantNodes().OfType<XComment>().Any(c => c.Value.Contains("原有注释")), "读取保留注释");
    Check(ConfigValidator.Validate(document, new()).Any(i => i.Message.Contains("元件类型未加载") && i.Severity == "警告"), "缺少 DLL 仍能打开并提示");
    var node = document.Settings.Single();
    ConfigDocument.SetValue(node, "Chinese", "已修改 & < > \" 中文");
    Check(document.IsDirty, "参数修改标记未保存");
    var backup = document.Save(path);
    Check(backup is not null && File.ReadAllText(backup) == original, "覆盖前完整备份");
    Check(Path.GetDirectoryName(backup) == Path.Combine(workspace, ConfigDocument.BackupFolder)
        && !Directory.EnumerateFiles(workspace, "*.bak").Any(), "备份放在同级 backup 子目录，machine.xml 旁边不留 .bak");
    Check(!document.IsDirty, "保存清除修改状态");
    var reloaded = ConfigDocument.Open(path);
    Check(ConfigDocument.Value(reloaded.Settings.Single(), "Chinese") == "已修改 & < > \" 中文", "中文及 XML 特殊字符正确往返");
    Check(reloaded.Settings.Single().Element("VendorExtension")?.Attribute("Mode")?.Value == "keep"
        && reloaded.Settings.Single().Attribute("Extra")?.Value == "保留", "保存保留未知节点和属性");
    File.AppendAllText(path, "\r\n<!-- 外部修改 -->");
    var blocked = false;
    try { reloaded.Save(path); } catch (IOException) { blocked = true; }
    Check(blocked, "防止覆盖其他程序的修改");
    var copyPath = Path.Combine(workspace, "copy.xml");
    reloaded.Save(copyPath);
    Check(File.Exists(copyPath) && reloaded.FilePath == copyPath, "可另存为继续编辑");
    File.WriteAllText(Path.Combine(workspace, "xxe.xml"), "<!DOCTYPE ArrayOfSetting [<!ENTITY e SYSTEM 'file:///C:/Windows/win.ini'>]><ArrayOfSetting>&e;</ArrayOfSetting>");
    blocked = false;
    try { ConfigDocument.Open(Path.Combine(workspace, "xxe.xml")); } catch (System.Xml.XmlException) { blocked = true; }
    Check(blocked, "machine.xml 解析拒绝外部实体");
    var csv = Path.Combine(workspace, "DI.csv");
    File.WriteAllText(csv, "Description,Name,Component,Index,Module\n\"描述,含逗号\",DoorOpen,Door,100,Chamber1\n正常,Emo,Emo,0,Safety\n");
    var points = IoPointReader.Read(workspace, "DI");
    Check(points.Count == 2 && points[0].Index == "100" && points[0].Description == "描述,含逗号", "点表按列名读取，支持 CSV 引号和逗号");
    Check(IoPointReader.KindFor("DiOpenedIndex") == "DI" && IoPointReader.KindFor("DoIndex") == "DO", "IO 属性匹配正确点表");

    // 新增、复制、追加参数、删除节点后，按原文件的 Tab 缩进和 CRLF 换行排版，不挤成一行。
    var layoutPath = Path.Combine(workspace, "layout.xml");
    File.WriteAllText(layoutPath, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<ArrayOfSetting>\r\n\t<!-- 注释 -->\r\n\t<Setting Name=\"Group\">\r\n"
        + "\t\t<Setting Name=\"A\" Type=\"t.A\">\r\n\t\t\t<Value Name=\"DoIndex\" Value=\"1\" />\r\n\t\t</Setting>\r\n\t</Setting>\r\n"
        + "\t<Setting Name=\"Empty\" />\r\n</ArrayOfSetting>");
    var layout = ConfigDocument.Open(layoutPath);
    var group = layout.Settings.Single(n => ConfigDocument.Attribute(n, "Name") == "Group");
    var a = layout.Settings.Single(n => ConfigDocument.Attribute(n, "Name") == "A");
    var aCopy = new XElement(a);
    aCopy.SetAttributeValue("Name", "ACopy");
    XmlLayout.AddAfter(a, aCopy);
    ConfigDocument.SetValue(a, "DiIndex", "5");
    var b = new XElement("Setting", new XAttribute("Name", "B"), new XAttribute("Type", "t.B"));
    ConfigDocument.SetValue(b, "DoIndex", "2");
    ConfigDocument.SetValue(b, "DiIndex", "3");
    XmlLayout.Add(group, b);
    XmlLayout.Add(layout.Settings.Single(n => ConfigDocument.Attribute(n, "Name") == "Empty"), new XElement("Setting", new XAttribute("Name", "C")));
    layout.Save(layoutPath);
    var expectedLayout = "<ArrayOfSetting>\r\n\t<!-- 注释 -->\r\n\t<Setting Name=\"Group\">\r\n"
        + "\t\t<Setting Name=\"A\" Type=\"t.A\">\r\n\t\t\t<Value Name=\"DoIndex\" Value=\"1\" />\r\n\t\t\t<Value Name=\"DiIndex\" Value=\"5\" />\r\n\t\t</Setting>\r\n"
        + "\t\t<Setting Name=\"ACopy\" Type=\"t.A\">\r\n\t\t\t<Value Name=\"DoIndex\" Value=\"1\" />\r\n\t\t</Setting>\r\n"
        + "\t\t<Setting Name=\"B\" Type=\"t.B\">\r\n\t\t\t<Value Name=\"DoIndex\" Value=\"2\" />\r\n\t\t\t<Value Name=\"DiIndex\" Value=\"3\" />\r\n\t\t</Setting>\r\n"
        + "\t</Setting>\r\n\t<Setting Name=\"Empty\">\r\n\t\t<Setting Name=\"C\" />\r\n\t</Setting>\r\n</ArrayOfSetting>";
    var savedLayout = File.ReadAllText(layoutPath);
    Check(savedLayout.EndsWith(expectedLayout, StringComparison.Ordinal), "新增、复制、追加参数按原文件缩进各占一行");
    Check(!savedLayout.Replace("\r\n", "").Contains('\n'), "保存保留原文件的 CRLF 换行");
    var reopened = ConfigDocument.Open(layoutPath);
    XmlLayout.Remove(reopened.Settings.Single(n => ConfigDocument.Attribute(n, "Name") == "B"));
    reopened.Save(layoutPath);
    var bBlock = "\t\t<Setting Name=\"B\" Type=\"t.B\">\r\n\t\t\t<Value Name=\"DoIndex\" Value=\"2\" />\r\n\t\t\t<Value Name=\"DiIndex\" Value=\"3\" />\r\n\t\t</Setting>\r\n";
    Check(File.ReadAllText(layoutPath).EndsWith(expectedLayout.Replace(bBlock, ""), StringComparison.Ordinal), "删除节点不留空行");

    // 改元件名：ec.xml、编号表、数据曲线、机械手示教点、点表、machine.xml 里别处写的路径都不会跟着改，校验和保存前要列出来。
    var renameDir = Path.Combine(workspace, "rename");
    Directory.CreateDirectory(Path.Combine(renameDir, "IO"));
    File.WriteAllText(Path.Combine(renameDir, "machine.xml"), "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<ArrayOfSetting>\r\n"
        + "\t<Setting Name=\"DataChart\" Type=\"t.Chart\">\r\n\t\t<Value Name=\"Exclude\" Value=\"Other, Chamber1.Door\" />\r\n\t</Setting>\r\n"
        + "\t<Setting Name=\"Light\" Type=\"t.Light\" />\r\n"
        + "\t<Setting Name=\"Chamber\">\r\n\t\t<Setting Name=\"Chamber1\" Type=\"t.Chamber\">\r\n"
        + "\t\t\t<Setting Name=\"Door\" Type=\"t.Cylinder\">\r\n\t\t\t\t<Value Name=\"DoOpenIndex\" Value=\"100\" />\r\n"
        + "\t\t\t\t<Value Name=\"DiOpenedIndex\" Value=\"-1\" />\r\n\t\t\t</Setting>\r\n\t\t</Setting>\r\n\t</Setting>\r\n"
        + "\t<Setting Name=\"Robot1\" Type=\"t.Robot\">\r\n\t\t<Setting Name=\"Size12\">\r\n\t\t\t<Setting Name=\"Chamber1\" Type=\"xyz.Elements.Elements.TeachPointElement\">\r\n"
        + "\t\t\t\t<Value Name=\"Number\" Value=\"3\" />\r\n\t\t\t</Setting>\r\n\t\t</Setting>\r\n\t</Setting>\r\n</ArrayOfSetting>");
    File.WriteAllText(Path.Combine(renameDir, "ec.xml"), "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<ArrayOfSetting>\r\n"
        + "  <Setting Name=\"Chamber1\">\r\n    <Value Name=\"A\" Value=\"5\" Default=\"3\" />\r\n"
        + "    <Setting Name=\"Door\">\r\n      <Value Name=\"B\" Value=\"1\" Default=\"1\" />\r\n    </Setting>\r\n  </Setting>\r\n</ArrayOfSetting>");
    File.WriteAllText(Path.Combine(renameDir, "SvDefinitions.xml"), "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<SvDefinitions>\r\n"
        + "  <Sv SVID=\"30000\" Name=\"Chamber1.State\" Enabled=\"true\" />\r\n  <Sv SVID=\"30001\" Name=\"Chamber1.Door.IsOpen\" Enabled=\"true\" />\r\n"
        + "  <Sv SVID=\"30002\" Name=\"Chamber1.Old\" Enabled=\"false\" />\r\n  <Sv SVID=\"30003\" Name=\"Chamber10.State\" Enabled=\"true\" />\r\n</SvDefinitions>");
    File.WriteAllText(Path.Combine(renameDir, "IO", "DO.csv"),
        "Description,Name,Component,Index,Module\n门开,DoorOpen,Door,100,Chamber1\n盖子,LidOpen,Lid,101,Chamber1\n别的,X,X,102,Chamber2\n");
    var renameDoc = ConfigDocument.Open(Path.Combine(renameDir, "machine.xml"));
    XElement Named(string name) => renameDoc.Settings.Single(n => ConfigDocument.Attribute(n, "Name") == name && n.Parent?.Attribute("Name")?.Value != "Size12");
    Check(RenameImpact.Find(renameDoc).Count == 0, "刚打开没有改名");
    Named("Chamber").SetAttributeValue("Name", "Chambers");
    var lightCopy = new XElement(Named("Light"));
    lightCopy.SetAttributeValue("Name", "LightCopy");
    XmlLayout.AddAfter(Named("Light"), lightCopy);
    Check(RenameImpact.Find(renameDoc).Count == 0, "改分组名（不进元件路径）、复制出来的节点都不算改名");
    Named("Chamber1").SetAttributeValue("Name", "PM1");
    var renames = RenameImpact.Find(renameDoc);
    Check(renames.Count == 1 && renames[0].OldPath == "Chamber1" && renames[0].NewPath == "PM1", "改模块名按后端元件路径（不含分组）识别");
    var impacts = string.Join("\n", renames[0].Impacts);
    Check(impacts.Contains("2 个 EC 值（1 个现场调过）"), "列出 ec.xml 旧名字下的 EC 值和调过的个数");
    Check(impacts.Contains("2 个 SVID") && !impacts.Contains("3 个 SVID"), "按旧路径数编号（停用的、名字前缀相同的别的模块不算）");
    Check(impacts.Contains("1 个 IO 点") && impacts.Contains("2 个 SV）"), "列出数据曲线受影响的信号");
    Check(impacts.Contains("Robot1.Size12.Chamber1"), "列出要跟着改名的机械手示教点");
    Check(impacts.Contains("DO.csv 2 行"), "列出点表里还写着旧名字的行");
    Check(impacts.Contains("DataChart.Exclude"), "列出 machine.xml 里别处写的旧路径");
    Check(ConfigValidator.Validate(renameDoc, new()).Count(i => i.Severity == "警告" && i.Message.StartsWith("由 Chamber1 改名为 PM1")) == renames[0].Impacts.Count,
        "改名影响逐条进校验结果");
    Named("PM1").SetAttributeValue("Name", "chamber1");
    Check(RenameImpact.Find(renameDoc).Count == 0, "只改大小写不算改名");
    Named("chamber1").SetAttributeValue("Name", "PM1");
    renameDoc.Save(renameDoc.FilePath);
    Check(RenameImpact.Find(renameDoc).Count == 0, "保存后新名字成了原名，同一次改名不再重复提示");

    if (args.Length >= 2)
    {
        var machine = ConfigDocument.Open(args[0]);
        var catalog = ElementCatalog.Read(args.Skip(1));
        Console.WriteLine($"元件数：{catalog.Elements.Count}；加载提示：{catalog.Diagnostics.Count}");
        foreach (var diagnostic in catalog.Diagnostics) Console.WriteLine(diagnostic);
        Check(catalog.Elements.ContainsKey("xyz.Elements.Elements.CylinderElement"), "现有平台元件从 DLL 发现");
        Check(catalog.Elements.ContainsKey("xyz._35021.Module.Clean.ChamberModule"), "现有机型 DLL 发现");
        var cylinder = catalog.Elements["xyz.Elements.Elements.CylinderElement"];
        Check(cylinder.Properties.Any(p => p.Name == "DoOpenIndex" && p.Required && p.DefaultValue == "-1"), "正确读取基类 [Machine] 及必填默认值");
        var digital = catalog.Elements["xyz.Elements.Elements.DiSensorElement"];
        var trigger = digital.Properties.Single(p => p.Name == "TriggerValue");
        Check(trigger.Kind == "System.Boolean" && trigger.Choices.SequenceEqual(new[] { "true", "false" }), "DI 触发电平读取为 true/false 布尔选项");
        Check(ConfigValidator.CheckValue(trigger, "false") is null && ConfigValidator.CheckValue(trigger, "True") is null
            && ConfigValidator.CheckValue(trigger, "Low") is not null && ConfigValidator.CheckValue(trigger, "High") is not null
            && ConfigValidator.CheckValue(trigger, "bad") is not null, "DI 触发电平只认 true/false，High/Low 和乱填的都被拦截");
        var initialIssues = ConfigValidator.Validate(machine, catalog);
        Check(initialIssues.All(i => i.Severity != "错误"), "现有 machine.xml 通过校验");
        Check(initialIssues.All(i => !i.Message.Contains("元件类型未加载")), "现有 machine.xml 所有元件类型已解析");
        var motor = machine.Settings.First(n => ConfigDocument.Attribute(n, "Name") == "SpinMotor");
        Check(ConfigDocument.PathOf(motor) == "Chamber.Chamber1.SpinMotor", "XML 配置路径保留分组层级");
        var door = machine.Settings.First(n => ConfigDocument.Attribute(n, "Name") == "Door");
        ConfigDocument.SetValue(door, "DoOpenIndex", "abc");
        Check(ConfigValidator.Validate(machine, catalog).Any(i => i.Severity == "错误" && i.Message.Contains("DoOpenIndex")), "拦截 IO 索引格式错误");
        ConfigDocument.SetValue(door, "DoOpenIndex", "100");
        // 机械手示教点是普通元件（TeachPointElement）：参数从元件的 [Machine] 读，按普通元件校验
        Check(catalog.Elements.TryGetValue("xyz.Elements.Elements.TeachPointElement", out var teachPointSpec)
            && new[] { "Number", "Y", "Direction", "PickArms", "PlaceArms" }.All(name => teachPointSpec.Properties.Any(p => p.Name == name)),
            "示教点元件从 DLL 发现，参数是站号、伸出距离、方向、取放手指");
        var teachPoint = machine.Settings.First(n => ConfigDocument.Attribute(n, "Type") == "xyz.Elements.Elements.TeachPointElement");
        Check(ConfigDocument.PathOf(teachPoint).StartsWith("Robot.Robot1.Size12.", StringComparison.Ordinal), "现有 machine.xml 的示教点挂在机械手的 Size12 分组下");
        ConfigDocument.SetValue(teachPoint, "Number", "abc");
        Check(ConfigValidator.Validate(machine, catalog).Any(i => i.Severity == "错误" && i.Message.Contains("Number")), "示教点站号不是整数：拦下");
        ConfigDocument.SetValue(teachPoint, "Number", "1");
        ConfigDocument.SetValue(teachPoint, "Direction", "Invalid");
        Check(ConfigValidator.Validate(machine, catalog).Any(i => i.Severity == "错误" && i.Message.Contains("Direction")), "拦截无效的伸出方向");
        ConfigDocument.SetValue(teachPoint, "Direction", "South");
        ConfigDocument.SetValue(teachPoint, "Arms", "1");
        Check(ConfigValidator.Validate(machine, catalog).Any(i => i.Severity == "警告" && i.Message.StartsWith("Arms 没有对应的 [Machine] 属性")),
            "示教点上写了老名字 Arms：提示没有这个参数");
        Check(initialIssues.All(i => !i.Message.Contains("被多个")), "现有 machine.xml 没有重复占用的 IO 点和轴数据块");
        // 原样复制一个腔体（IO 索引、轴数据块全重复）：后端会报"轴命令块路径重复"起不来，校验必须拦下。
        var duplicated = ConfigDocument.Open(args[0]);
        var chamber = duplicated.Settings.First(n => ConfigDocument.PathOf(n) == "Chamber.Chamber1");
        var chamberCopy = new XElement(chamber);
        chamberCopy.SetAttributeValue("Name", "Chamber1Copy");
        XmlLayout.AddAfter(chamber, chamberCopy);
        var duplicateIssues = ConfigValidator.Validate(duplicated, catalog).Where(i => i.Severity == "错误").ToList();
        Check(duplicateIssues.Any(i => i.Message.StartsWith("DO 100 ")), "复制腔体后拦截重复的 DO 索引");
        Check(duplicateIssues.Any(i => i.Message.Contains("轴命令块 .Motor_Csharp2Plc[10]")), "复制腔体后拦截重复的轴命令块");
        Check(duplicateIssues.Any(i => i.Path == "Chamber.Chamber1Copy.Door") && duplicateIssues.Any(i => i.Path == "Chamber.Chamber1.Door"),
            "重复的两处都列出来，双击能分别定位");
        // 工艺配方的两张字段表：真 machine.xml 的没有错误；几种典型配错都拦下（后端开机时会报同样的错起不来）
        Check(initialIssues.All(i => !(i.Severity == "错误" && i.Path.StartsWith("ProcessRecipe", StringComparison.Ordinal))), "现有 machine.xml 的工艺配方配方头、字段表通过校验");
        Check(RecipeFields.StationTypes(machine, catalog).SequenceEqual(["A"]), "腔体配的 Type：现有 machine.xml 的 4 个腔体都是 A");
        XElement FieldOf(ConfigDocument doc, string key) => doc.Settings.First(n => ConfigDocument.PathOf(n) == "ProcessRecipe.Fields." + key);
        XElement HeadOf(ConfigDocument doc, string key) => doc.Settings.First(n => ConfigDocument.PathOf(n) == "ProcessRecipe.Head." + key);
        List<ValidationIssue> RecipeErrors(ConfigDocument doc) => ConfigValidator.Validate(doc, catalog).Where(i => i.Severity == "错误" && i.Path.StartsWith("ProcessRecipe", StringComparison.Ordinal)).ToList();
        var recipeDoc = ConfigDocument.Open(args[0]);
        ConfigDocument.SetValue(FieldOf(recipeDoc, "Mode"), "Default", "Spin");
        Check(RecipeErrors(recipeDoc).Any(i => i.Path.EndsWith(".Mode") && i.Message.Contains("不在 Source.A 的选项")), "下拉的默认值不在 Type A 的选项里：拦下");
        ConfigDocument.SetValue(FieldOf(recipeDoc, "Mode"), "Default", "Time");
        var chemicalOptions = ConfigDocument.Value(FieldOf(recipeDoc, "Chemical"), "Source.A") ?? "";
        ConfigDocument.SetValue(FieldOf(recipeDoc, "Chemical"), "Source.A", "DIW,diw");
        Check(RecipeErrors(recipeDoc).Any(i => i.Path.EndsWith(".Chemical") && i.Message.Contains("两遍")), "下拉的选项写了两遍（不分大小写）：拦下");
        ConfigDocument.SetValue(FieldOf(recipeDoc, "Chemical"), "Source.A", " , ");
        Check(RecipeErrors(recipeDoc).Any(i => i.Path.EndsWith(".Chemical") && i.Message.Contains("Source.A") && i.Message.Contains("要写选项")),
            "腔体有 Type A、Type A 那一套一个选项都没有：拦下");
        ConfigDocument.SetValue(FieldOf(recipeDoc, "Chemical"), "Source.A", chemicalOptions);
        ConfigDocument.SetValue(FieldOf(recipeDoc, "Chemical"), "Source.B", "DIW");
        Check(RecipeErrors(recipeDoc).Count == 0 && ConfigValidator.Validate(recipeDoc, catalog).Any(i => i.Severity == "警告" && i.Path.EndsWith(".Chemical") && i.Message.Contains("Source.B")),
            "写了没有腔体用的 Type B 那一套：只给提示");
        XmlLayout.Remove(FieldOf(recipeDoc, "Chemical").Elements("Value").First(v => ConfigDocument.Attribute(v, "Name") == "Source.B"));
        ConfigDocument.SetValue(HeadOf(recipeDoc, "Timeout"), "Type", "Int");
        Check(RecipeErrors(recipeDoc).Any(i => i.Path.EndsWith(".Timeout") && i.Message.Contains("Double")), "配方头固定的超时时间类型写错：拦下");
        ConfigDocument.SetValue(HeadOf(recipeDoc, "Timeout"), "Type", "Double");
        var timeoutNode = HeadOf(recipeDoc, "Timeout");
        XmlLayout.Remove(timeoutNode);
        Check(RecipeErrors(recipeDoc).Any(i => i.Path == "ProcessRecipe.Head" && i.Message.Contains("Timeout")), "配方头少了固定的超时时间：拦下");
        XmlLayout.Add(recipeDoc.Settings.First(n => ConfigDocument.PathOf(n) == "ProcessRecipe.Head"), timeoutNode);
        ConfigDocument.SetValue(FieldOf(recipeDoc, "Rpm"), "Min", "10");
        ConfigDocument.SetValue(FieldOf(recipeDoc, "Rpm"), "Max", "5");
        Check(RecipeErrors(recipeDoc).Any(i => i.Path.EndsWith(".Rpm") && i.Message.Contains("比上限")), "下限比上限大：拦下");
        ConfigDocument.SetValue(FieldOf(recipeDoc, "Rpm"), "Min", "0");
        ConfigDocument.SetValue(FieldOf(recipeDoc, "Rpm"), "Max", "3000");
        ConfigDocument.SetValue(FieldOf(recipeDoc, "Seconds"), "Type", "Int");
        Check(RecipeErrors(recipeDoc).Any(i => i.Path.EndsWith(".Seconds") && i.Message.Contains("Double")), "时间不是小数：拦下");
        ConfigDocument.SetValue(FieldOf(recipeDoc, "Seconds"), "Type", "Double");
        ConfigDocument.SetValue(FieldOf(recipeDoc, "Arm"), "Unit", "mm");
        Check(RecipeErrors(recipeDoc).Count == 0 && ConfigValidator.Validate(recipeDoc, catalog).Any(i => i.Severity == "警告" && i.Path.EndsWith(".Arm") && i.Message.Contains("Unit")),
            "改回来就没错误；下拉配了单位只给提示");
        XmlLayout.Remove(recipeDoc.Settings.First(n => ConfigDocument.PathOf(n) == "ProcessRecipe.Fields"));
        Check(RecipeErrors(recipeDoc).Any(i => i.Path == "ProcessRecipe" && i.Message.Contains("Fields")), "工艺配方元件下没有字段表：拦下");
        var split = RecipeFields.SplitOptions(" DIW , SC1,,HF ");
        Check(split.SequenceEqual(["DIW", "SC1", "HF"]) && RecipeFields.JoinOptions([" DIW ", "", "SC1", "HF"]) == "DIW,SC1,HF",
            "选项按英文逗号拆成一行一个（去空白、丢空的），连回去照先后、英文逗号隔开");
        Check(RecipeFields.CheckValue("Double", "1.25", 0, 3, 1) is not null && RecipeFields.CheckValue("Double", "1.50", 0, 3, 1) is null
            && RecipeFields.CheckValue("Int", "1.5", null, null, null) is not null && RecipeFields.CheckValue("Bool", "yes", null, null, null) is not null,
            "值的检查跟后端一样：小数位、整数、开关");
        var renamed = ConfigDocument.Open(args[0]);
        renamed.Settings.First(n => ConfigDocument.PathOf(n) == "Chamber.Chamber1").SetAttributeValue("Name", "PM1");
        var realRenames = RenameImpact.Find(renamed);
        Check(realRenames.Count == 1 && realRenames[0].Impacts.Any(i => i.Contains("Size12.Chamber1")) && realRenames[0].Impacts.Any(i => i.Contains("Size8.Chamber1"))
            && realRenames[0].Impacts.Any(i => i.StartsWith("ec.xml")) && realRenames[0].Impacts.Any(i => i.StartsWith("数据曲线")),
            "现有 machine.xml 改腔体名：提示两种尺寸的示教点、ec.xml、数据曲线");
        var loadedBytes = File.ReadAllBytes(args[0]);
        var unchanged = ConfigDocument.Open(args[0]);
        var roundTrip = Path.Combine(workspace, "actual.xml");
        unchanged.Save(roundTrip);
        Check(XNode.DeepEquals(ConfigDocument.Open(roundTrip).Xml.Root, unchanged.Xml.Root), "整份现有 machine.xml 往返保留结构和注释");
        Check(File.ReadAllText(roundTrip) == File.ReadAllText(args[0]).TrimStart('﻿'), "没改的 machine.xml 原样存回（逐字一致，换行不变）");
        Check(File.ReadAllBytes(args[0]).SequenceEqual(loadedBytes), "验证未修改用户原 machine.xml");
    }
    Console.WriteLine($"全部通过：{passed} 项。");
}
finally
{
    // 仅删除本进程创建的 GUID 临时目录。
    Directory.Delete(workspace, true);
}
