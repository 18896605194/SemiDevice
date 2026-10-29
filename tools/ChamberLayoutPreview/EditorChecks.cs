using System.IO;
using System.Xml.Linq;
using System.Windows.Media.Media3D;
using ChamberLayoutEditor;

namespace ChamberLayoutPreview;

internal static class EditorChecks
{
    public static void Run(ChamberEditorPage page, string output)
    {
        var checks = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add("PASS " + name); }
        var model = page.ViewModel;
        string initial = model.PrepareSave();
        Check(model.Parts.Count(p => p.IsArm) == 2 && !model.IsDirty, "默认示例包含两套摆臂，初始状态未修改");
        Check(ChamberLayoutDocument.Parse(initial).ToXml() == initial, "布局 XML 保存和重载一致");
        var arm = model.Parts.First(p => p.IsArm);
        model.Selected = arm;
        model.BeginDrag(); arm.X = -2.6; arm.Z = -1.8; model.EndDrag();
        model.UndoCommand.Execute(null);
        Check(model.PrepareSave() == initial && !model.IsDirty, "一次拖动只占一次撤销，撤销后恢复保存状态");
        model.RedoCommand.Execute(null);
        Check(model.Selected!.X == -2.6 && model.Selected.Z == -1.8 && model.IsDirty, "重做恢复拖动坐标");
        string moved = model.PrepareSave();
        model.BeginDrag(); model.Selected.X = 3; model.CancelDrag();
        Check(model.PrepareSave() == moved, "取消拖动恢复开始前的坐标");
        model.Selected!.Yaw = 35; model.Selected.HomeAngle = -120;
        Program.Pump(100);
        var scene = (ChamberLayoutScene)page.FindName("Scene");
        model.IsPreview = true; model.Reach = 1; Program.Pump(350);
        var probe = scene.Probe(model.Selected.Id);
        Check(probe.CenterError < 1e-6, "移动并旋转安装方向后，摆臂末端仍对准盘心");
        model.Reach = 0; Program.Pump(350); probe = scene.Probe(model.Selected.Id);
        Check(probe.HomeError < 1e-6, "改变 Home 方向后，接液杯仍位于臂尖下方");
        double beforeHeight = probe.TipHeight;
        model.Lift = true; Program.Pump(350); probe = scene.Probe(model.Selected.Id);
        Check(probe.TipHeight > beforeHeight + 0.2 && probe.HomeError < 1e-6, "升降带动摆臂，接液杯水平位置保持固定");
        model.IsPreview = false; Program.Pump(350);
        Check(model.PrepareSave() == ChamberLayoutDocument.Parse(model.PrepareSave()).ToXml(), "模拟动作没有写入布局文件");
        File.WriteAllText(Path.Combine(output, "test-layout.xml"), model.PrepareSave());
        Check(ChamberLayoutDocument.Parse(File.ReadAllText(Path.Combine(output, "test-layout.xml"))).ToXml() == model.PrepareSave(), "文件保存后重新加载保留位置和方向");
        bool rejected = false;
        try { ChamberLayoutDocument.Parse(initial.Replace("ArmAssembly", "UnknownPart")); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "未知组件被拒绝");
        rejected = false;
        try { ChamberLayoutDocument.Parse(initial.Replace("X=\"1.95\"", "X=\"NaN\"")); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "非有限坐标被拒绝");
        model.Load(ChamberLayoutDocument.Parse(initial));
        var document = model.Document!;
        model.Selected = model.Parts.First(p => p.IsArm); model.Selected.X = 0; model.Selected.Z = 0;
        rejected = false;
        try { model.PrepareSave(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && model.Error.Length > 0, "无效安装位置显示错误并阻止保存");
        model.UndoCommand.Execute(null);
        Check(model.Error.Length == 0, "无效位置可以撤销恢复");
        model.Load(ChamberLayoutDocument.Parse(initial));

        arm = model.Parts.First(p => p.IsArm);
        var otherArm = model.Parts.Last(p => p.IsArm);
        void Set(ChamberPlacement part, string key, double value) => part.Dimensions.Single(d => d.Key == key).Value = value;
        bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;
        arm.AutoLength = false;
        Set(arm, "ArmLength", 3.1); Set(arm, "ArmWidth", 0.28);
        Set(arm, "CupRadius", 0.48); Set(arm, "CupHeight", 0.72);
        Set(arm, "Nozzle1.DiameterScale", 1.8); Set(arm, "Nozzle1.LengthScale", 0.9);
        Program.Pump(100);
        var measured = scene.ProbeDimensions(arm.Id);
        var otherMeasured = scene.ProbeDimensions(otherArm.Id);
        Check(Near(measured["ArmLength"], 3.1) && Near(measured["ArmWidth"], 0.28), "手动臂长、宽度实际应用到三维组件");
        Check(Near(measured["CupRadius"], 0.48) && Near(measured["CupHeight"], 0.72)
            && Near(otherMeasured["CupRadius"], 0.224), "接液杯半径和高度独立调整，其他摆臂的接液杯不受影响");
        Check(Near(measured["Nozzle1.DiameterScale"], 1.8) && Near(measured["Nozzle2.DiameterScale"], 1)
            && Near(measured["Nozzle1.Length"], 3.1 * 0.9) && Near(measured["Nozzle2.Length"], 3.1), "每路喷嘴的管径和长度可单独调整");
        model.Selected = arm;
        model.BeginDrag(); arm.X = 2.6; model.EndDrag(); Program.Pump(100);
        Check(Near(scene.ProbeDimensions(arm.Id)["ArmLength"], 3.1) && model.Warning.Length > 0,
            "手动臂长在拖动安装位置后保持不变，并提示中心位置偏差");
        model.IsPreview = true; model.Reach = 1; Program.Pump(350);
        Check(scene.Probe(arm.Id).CenterError > 0.1 && Near(scene.ProbeDimensions(arm.Id)["ArmLength"], 3.1),
            "中心预览保留手动臂长，不强制拉伸到盘心");
        model.Reach = 0; Program.Pump(350);
        Check(scene.Probe(arm.Id).HomeError < 1e-6, "手动改变臂长后接液杯跟随 Home 末端位置");
        double oldHeight = scene.Probe(arm.Id).TipHeight;
        Set(arm, "LiftHeightScale", 1.5); Program.Pump(100);
        Check(scene.Probe(arm.Id).TipHeight > oldHeight + 0.2 && Near(scene.ProbeDimensions(arm.Id)["CupHeight"], 0.72),
            "单独改变 Lift 高度带动安装面，接液杯尺寸保持不变");
        model.IsPreview = false;

        var diskPart = model.Parts.Single(p => p.Kind == "Disk");
        var bowlPart = model.Parts.Single(p => p.Kind == "Bowl");
        var doorPart = model.Parts.Single(p => p.Kind == "Door");
        Set(diskPart, "Radius", 1.1); Set(diskPart, "Thickness", 0.08);
        Set(bowlPart, "Radius", 1.8); Set(bowlPart, "HeightScale", 1.4);
        Set(doorPart, "Width", 2.8); Set(doorPart, "Height", 0.75);
        model.Document!.BaseLength = 7.2; model.Document.BaseWidth = 5.4; model.Document.BaseThickness = 0.25;
        Program.Pump(350);
        Check(Near(scene.ProbeDimensions(diskPart.Id)["Radius"], 1.1) && Near(scene.ProbeDimensions(diskPart.Id)["Thickness"], 0.08)
            && Near(scene.ProbeDimensions(bowlPart.Id)["Radius"], 1.8), "卡盘半径、盘厚和 Bowl 半径可独立调整");
        Check(Near(scene.ProbeDimensions(doorPart.Id)["Width"], 2.8) && Near(scene.ProbeDimensions(doorPart.Id)["Height"], 0.75),
            "腔门宽度、高度实际应用到三维组件");
        var baseSize = scene.ProbeBase();
        Check(Near(baseSize.Length, 7.2) && Near(baseSize.Width, 5.4) && Near(baseSize.Thickness, 0.25)
            && Near(scene.ProbeDimensions(arm.Id)["ArmLength"], 3.1), "底座长宽厚度独立调整，不缩放内部组件");
        string resized = model.PrepareSave();
        Check(ChamberLayoutDocument.Parse(resized).ToXml() == resized, "全部尺寸和手动臂长模式能够保存并完整重载");
        model.UndoCommand.Execute(null); Program.Pump(100);
        Check(Near(scene.ProbeBase().Thickness, 0.12), "底座尺寸变化纳入撤销历史");
        model.RedoCommand.Execute(null); Program.Pump(100);
        Check(model.PrepareSave() == resized, "重做恢复底座尺寸和全部配置");

        var legacy = XDocument.Parse(initial);
        legacy.Root!.SetAttributeValue("Version", "1");
        legacy.Root.SetAttributeValue("BaseLength", 5.8);
        legacy.Root.SetAttributeValue("BaseWidth", 4.6);
        legacy.Root.Elements("Base").Remove();
        foreach (var partElement in legacy.Root.Elements()) { partElement.Attribute("AutoLength")!.Remove(); partElement.Elements("Size").Remove(); }
        var imported = ChamberLayoutDocument.Parse(legacy.ToString());
        Check(Near(imported.BaseThickness, 0.12) && imported.Parts.Where(p => p.IsArm).All(p => p.AutoLength), "旧版无尺寸布局仍能加载默认外形");
        arm = model.Parts.First(p => p.IsArm);
        Set(arm, "CupRadius", -1);
        rejected = false;
        try { model.PrepareSave(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && model.Error.Length > 0, "非法尺寸阻止保存，并保留可撤销的编辑状态");
        model.UndoCommand.Execute(null);
        Check(model.Error.Length == 0, "非法尺寸能够撤销恢复");
        File.WriteAllText(Path.Combine(output, "resized-layout.xml"), model.PrepareSave());
        model.Load(ChamberLayoutDocument.Parse(initial));
        model.Lift = false; model.Spray = false; model.Spin = false; model.Reach = 0;
        Program.Pump(350);
        scene.DefaultView();
        Check(scene.ViewPreset == ChamberViewPreset.Perspective && !model.IsPreview, "默认采用三维编辑视角，模拟动作独立关闭");
        string beforeViewChange = model.PrepareSave();
        foreach (var preset in Enum.GetValues<ChamberViewPreset>())
        {
            scene.SetViewPreset(preset);
            var anchor = new Point3D(1.2, 0.6, -0.8);
            var delta = preset switch
            {
                ChamberViewPreset.Front or ChamberViewPreset.Back => new Vector3D(0.2, 0.1, 0),
                ChamberViewPreset.Left or ChamberViewPreset.Right => new Vector3D(0, 0.1, 0.2),
                _ => new Vector3D(0.2, 0, 0.1)
            };
            var projected = scene.Project(anchor + delta);
            var recovered = scene.Unproject(projected, anchor);
            Check((recovered - anchor - delta).Length < 1e-6, $"{preset} 视角投影和拖动坐标转换一致，无无效数值");
        }
        scene.SetViewPreset(ChamberViewPreset.Top);
        model.IsPreview = true;
        Check(scene.ViewPreset == ChamberViewPreset.Top, "切换模拟动作不覆盖用户选择的视角");
        model.IsPreview = false;
        scene.DefaultView();
        Check(model.PrepareSave() == beforeViewChange && !model.IsDirty, "切换六种视角不修改布局、尺寸或保存状态");

        var basePart = model.Document!.BasePart;
        Check(model.Parts.Contains(basePart) && basePart.IsBase && basePart.Dimensions.Count == 3, "底盘作为独立控件列入组件列表并提供三项尺寸");
        scene.SetViewPreset(ChamberViewPreset.Top);
        var picked = scene.Pick(scene.Project(new Point3D(-2.6, -0.01, 0)));
        Check(ReferenceEquals(picked, basePart), "点击底盘实体能命中底盘组件");
        model.Selected = picked;
        Check(model.Selected?.IsBase == true, "底盘可以独立选中并使用统一属性面板");
        var origins = model.Parts.Where(p => !p.IsBase).ToDictionary(p => p.Id, p => scene.ProbeOrigin(p.Id));
        model.BeginDrag(); basePart.X = 0.6; basePart.Y = -0.1; basePart.Z = 0.4; model.EndDrag();
        basePart.Yaw = 20;
        Set(basePart, "Length", 7); Set(basePart, "Width", 5.2); Set(basePart, "Thickness", 0.2);
        Program.Pump(100);
        Check((scene.ProbeOrigin(basePart.Id) - new Point3D(0.6, -0.1, 0.4)).Length < 1e-6, "底盘的位置实际应用到三维控件");
        Check(origins.All(pair => (scene.ProbeOrigin(pair.Key) - pair.Value).Length < 1e-6), "移动旋转和缩放底盘不会带动其他组件");
        var baseLayout = ChamberLayoutDocument.Parse(model.PrepareSave());
        Check(baseLayout.BasePart.Yaw == 20 && Near(baseLayout.BasePart.X, 0.6) && Near(baseLayout.BaseLength, 7)
            && Near(baseLayout.BaseThickness, 0.2), "底盘独立位置、旋转和尺寸完整保存重载");
        Check(imported.Parts.Count(p => p.IsBase) == 1 && Near(imported.BaseLength, 5.8), "Version=1 布局自动迁移为独立底盘组件");
        var duplicatedBase = XDocument.Parse(initial);
        duplicatedBase.Root!.Add(new XElement(duplicatedBase.Root.Element("Base")!));
        rejected = false;
        try { ChamberLayoutDocument.Parse(duplicatedBase.ToString()); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "重复组件 ID 被拒绝");
        model.Load(ChamberLayoutDocument.Parse(initial));
        model.Selected = model.Document!.BasePart;
        scene.DefaultView();

        var originalOrigins = model.Parts.ToDictionary(p => p.Id, p => scene.ProbeOrigin(p.Id));
        model.SelectedComponentType = model.ComponentTypes.Single(t => t.Kind == "ArmAssembly");
        model.AddComponentCommand.Execute(null); Program.Pump(150);
        Check(model.Parts.Count(p => p.IsArm) == 3 && model.Selected!.Id == "Arm3" && model.IsDirty, "组件库添加第三套摆臂并自动编号和选中");
        Check(originalOrigins.All(pair => (scene.ProbeOrigin(pair.Key) - pair.Value).Length < 1e-6), "添加组件保留所有原组件的安装位置");
        string withArm = model.PrepareSave();
        model.UndoCommand.Execute(null);
        Check(model.PrepareSave() == initial && !model.IsDirty, "一次撤销移除新增组件并恢复原布局");
        model.RedoCommand.Execute(null); Program.Pump(150);
        Check(model.PrepareSave() == withArm, "重做完整恢复新增组件");
        model.IsPreview = true; model.Reach = 1; Program.Pump(350);
        Check(scene.Probe("Arm3").CenterError < 1e-6, "新增加的第三套摆臂参与三维动作预览");
        int countBeforePreviewAdd = model.Parts.Count;
        Check(!model.AddComponentCommand.CanExecute(null), "模拟动作期间禁用新增组件");
        model.AddComponentCommand.Execute(null);
        Check(model.Parts.Count == countBeforePreviewAdd, "禁用状态下执行新增也不改变布局");
        model.IsPreview = false;

        foreach (var definition in model.ComponentTypes.Where(t => t.Kind != "ArmAssembly"))
        {
            model.SelectedComponentType = definition;
            model.AddComponentCommand.Execute(null); Program.Pump(100);
            var added = model.Selected!;
            Check(added.Kind == definition.Kind && scene.ProbeDimensions(added.Id).Count > 0,
                $"组件库可新增并渲染 {definition.Kind}，提供独立尺寸");
        }
        Check(model.Parts.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == model.Parts.Count,
            "不同类型和重复类型的组件自动分配唯一 ID");
        var extraCup = model.Parts.Single(p => p.Kind == "HomeCup");
        Set(extraCup, "Radius", 0.46); Program.Pump(100);
        Check(Near(scene.ProbeDimensions(extraCup.Id)["Radius"], 0.46) && Near(scene.ProbeDimensions("Arm1")["CupRadius"], 0.224),
            "单独新增的接液杯尺寸不影响摆臂内的接液杯");
        var extraNozzle = model.Parts.Single(p => p.Kind == "Nozzle");
        extraNozzle.Component = "Arm3.Nozzle_DIW";
        Check(model.Error.Length == 0 && ChamberLayoutDocument.Parse(model.PrepareSave()).Parts.Single(p => p.Id == extraNozzle.Id).Component == "Arm3.Nozzle_DIW",
            "新增组件的关联元件路径可编辑和保存");
        var allComponents = model.PrepareSave();
        Check(ChamberLayoutDocument.Parse(allComponents).ToXml() == allComponents, "八种组件、多个底盘和多个卡盘能够完整保存重载");
        File.WriteAllText(Path.Combine(output, "components-layout.xml"), allComponents);
        model.Load(ChamberLayoutDocument.Parse(initial));
        model.SelectedComponentType = model.ComponentTypes[0];
        model.Selected = model.Parts.First(p => p.IsArm);
        model.Lift = false; model.Spray = false; model.Spin = false; model.Reach = 0;
        Program.Pump(350);
        File.WriteAllLines(Path.Combine(output, "editor-checks.txt"), checks);
    }
}
