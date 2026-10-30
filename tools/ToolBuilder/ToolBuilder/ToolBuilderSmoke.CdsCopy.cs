using CdsAssemblyPreview;
using System.Windows;
using System.Windows.Threading;

namespace ToolBuilder;

internal static partial class ToolBuilderSmoke
{
    private static async Task CopySensorChecks(MainViewModel vm, NodeModel module, PreviewPage editor,
        Action<bool, string> check, Func<string, Task> screenshot)
    {
        var model = editor.ViewModel;
        async Task Pump() { await Task.Delay(80); await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
        Vector Scale()
        {
            var transform = editor.Scene.Stage.TransformToAncestor(editor.Scene);
            return transform.Transform(new Point(100, 100)) - transform.Transform(new Point());
        }
        var original = CdsLayout.Parse(model.PrepareSave());
        string OriginalItems()
        {
            var kept = new CdsLayout { ComponentRoot = model.Layout.ComponentRoot, Width = model.Layout.Width, Height = model.Layout.Height };
            kept.Nodes.AddRange(model.Layout.Nodes.Where(n => original.Nodes.Any(o => o.Id == n.Id))); kept.Pipes.AddRange(model.Layout.Pipes);
            return kept.ToXml();
        }
        await screenshot("toolbuilder-cds-before-copy.png"); var scale = Scale();
        var copies = new List<NodeModel>();
        foreach (string tank in new[] { "Tank1", "Tank1", "Tank2" })
        {
            if (copies.Count == 2) { editor.Scene.ZoomPercent = 150; await Pump(); scale = Scale(); }
            vm.SelectNode(module.Walk().Single(n => n.Path == module.Path + "." + tank + ".LL"));
            vm.DuplicateCommand.Execute(null); copies.Add(vm.Selected!); await Pump();
            string component = CdsLayoutComposer.Relative(module.Path, copies[^1].Path);
            var added = model.Layout.Nodes.Single(n => n.Component == component);
            var owner = model.Layout.Nodes.Single(n => n.Component == tank);
            check(added.Kind == "Sensor" && model.SensorCount == 10 + copies.Count
                && added.X >= owner.X + owner.Width && added.X <= owner.X + owner.Width + 240
                && added.Y >= owner.Y && added.Y + added.Height <= model.Layout.Height,
                $"实际复制{tank}.LL后新增独立Sensor，放在所属Tank旁的可见空位");
            check(model.Layout.Width == original.Width && model.Layout.Height == original.Height && (Scale() - scale).Length < 0.001,
                $"复制第{copies.Count}个LL后画布尺寸及实际WPF显示比例保持不变");
            check(OriginalItems() == original.ToXml(), "复制LL保留原有组件的大小、位置和管路");
            var bounds = new Rect(added.X, added.Y, added.Width, added.Height);
            check(model.Layout.Nodes.Where(n => n.Id != added.Id).All(n => !bounds.IntersectsWith(new Rect(n.X, n.Y, n.Width, n.Height)))
                && bounds.Right <= model.Layout.Width && bounds.Bottom <= model.Layout.Height,
                "新Sensor位于画布内的可见空位，不覆盖已有组件或此前复制的Sensor");
        }
        model.Select(model.Layout.Nodes.Single(n => n.Component == CdsLayoutComposer.Relative(module.Path, copies[0].Path)).Id);
        model.SensorOnCommand.Execute(null);
        check(model.FindSensor(model.Selected!.Component)?.IsActive == true, "复制的LL可独立模拟触发");
        await screenshot("toolbuilder-cds-after-copy.png");
        var current = CdsLayout.Parse(model.PrepareSave());
        var restored = CdsLayoutComposer.Compose(model.Snapshot, current);
        check(restored.Width == original.Width && restored.Height == original.Height && restored.ToXml() == current.ToXml(),
            "复制Sensor后的布局保存重载保持画布尺寸、所有位置及连接不变");
        // 不移入后续用例；本测试只操作测试目录中的装机副本。
        vm.SelectNode(module);
        foreach (var copy in copies) { copy.Element.Remove(); copy.Parent!.Children.Remove(copy); }
        await Pump();
        check(model.Layout.ToXml() == original.ToXml() && model.SensorCount == 10, "删除复制的Sensor后原有布局和画布尺寸保持不变");
        editor.Scene.FitCommand.Execute(null); await Pump();
    }
}
