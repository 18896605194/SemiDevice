using System.IO;
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
        File.WriteAllLines(Path.Combine(output, "editor-checks.txt"), checks);
    }
}
