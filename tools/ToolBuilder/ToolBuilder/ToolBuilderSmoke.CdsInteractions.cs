using CdsAssemblyPreview;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ToolBuilder;

internal static partial class ToolBuilderSmoke
{
    private static async Task InteractionChecks(PreviewPage editor, Action<bool, string> check, Func<string, Task> screenshot)
    {
        var model = editor.ViewModel;
        var tank = model.Components.First(n => n.Kind == "Tank");
        var valve = model.Components.First(n => n.Kind == "Valve" && n.Component.Contains("Supply", StringComparison.Ordinal));
        string pipeId = model.Layout.Pipes[0].Id, original = model.Layout.ToXml(); int installed = model.Components.Count;
        async Task Pump() { await Task.Delay(60); await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
        async Task Click(Button button) { ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); await Pump(); }

        editor.Inspector.SelectedIndex = 1; model.SelectPipe(pipeId); await Pump();
        check(editor.Scene.Pipes[pipeId].IsSelected && editor.Scene.Overlay.Children.OfType<Polyline>().Any(), "编辑管路时正常显示选择高亮和辅助线");
        editor.Inspector.SelectedIndex = 2; await Pump();
        check(model.SelectedPipeId == pipeId && !editor.Scene.Pipes[pipeId].IsSelected && !editor.Scene.Overlay.Children.OfType<Polyline>().Any()
            && !editor.Scene.Overlay.Children.OfType<Rectangle>().Any(), "点击模拟动作立即隐藏管路高亮、虚线及拐点，保留操作对象");
        check(editor.Scene.Pipes[pipeId].IsFlowing && original == model.Layout.ToXml(), "隐藏编辑辅助不改变流动反馈或布局内容");
        await screenshot("toolbuilder-cds-pipe-simulation-clean.png");
        editor.Inspector.SelectedIndex = 0; await Pump();
        check(editor.Scene.Pipes[pipeId].IsSelected && editor.Scene.Overlay.Children.OfType<Polyline>().Any(), "返回安装布局恢复当前管路的编辑辅助");
        model.Select(valve.Id); editor.Inspector.SelectedIndex = 2; await Pump();
        await Click(editor.CloseValveButton); await Click(editor.OpenValveButton);
        check(editor.Scene.Valves[valve.Id].IsOpen == true && editor.Scene.Pipes[pipeId].IsFlowing
            && !editor.Scene.Overlay.Children.OfType<Border>().Any(b => b.BorderThickness.Left > 0), "模拟阀门启停保持反馈和流动可见，且不恢复选择框");
        model.Select(tank.Id); model.SelectedLevel = 3; await Pump();
        check(!editor.Scene.Tanks[tank.Id].IsSelected && editor.Scene.Tanks[tank.Id].FillFraction > 0
            && !editor.Scene.Overlay.Children.OfType<Border>().Any(b => b.BorderThickness.Left > 0), "槽的模拟液位正常显示且没有选择框遮挡");
        await screenshot("toolbuilder-cds-tank-simulation-clean.png");

        model.ConnectMode = true; model.Connect(valve.Id + ".Outlet"); editor.Inspector.SelectedIndex = 2; await Pump();
        check(!model.ConnectMode && model.StartPort is null && !editor.Scene.Overlay.Children.OfType<Ellipse>().Any(), "进入模拟动作取消待连接端口并隐藏连接标记");
        model.AddInterfaceCommand.Execute(null); await Pump();
        string terminal = model.Selected!.Id;
        check(model.Selected.Kind == "Terminal" && !model.IsSimulationMode && editor.DeleteSelectionButton.IsEnabled, "新增接口自动选中，并提供可用的删除按钮");
        check(!editor.TryDeleteSelection(Key.Delete, ModifierKeys.None, editor.PositionXInput) && model.Components.Any(n => n.Id == terminal), "输入坐标时Delete只处理输入内容，不删除外部接口");
        await Click(editor.DeleteSelectionButton);
        check(model.Components.Count == installed && model.Components.All(n => n.Id != terminal) && !model.HasSelection, "工具栏按钮能删除未连接的外部接口并清理选中状态");

        model.AddInterfaceCommand.Execute(null); terminal = model.Selected!.Id;
        model.ConnectMode = true;
        model.Connect(terminal + ".Port"); model.Connect(valve.Id + ".Inlet");
        model.Connect(terminal + ".Port"); model.Connect(valve.Id + ".Outlet");
        model.Connect(terminal + ".Port");
        check(model.Layout.Pipes.Count == 3 && model.StartPort == terminal + ".Port", "删除测试覆盖接口多条连线及待连接端口");
        check(editor.TryDeleteSelection(Key.Delete, ModifierKeys.None, editor.Scene.Stage), "画布Delete键执行外部接口删除"); await Pump();
        check(model.Layout.Pipes.Count == 1 && model.PipeItems.Count == 1 && editor.Scene.Pipes.Count == 1 && model.StartPort is null
            && model.Layout.Nodes.All(n => n.Id != terminal) && model.Layout.Pipes.All(p => CdsLayoutComposer.Owner(p.From) != terminal && CdsLayoutComposer.Owner(p.To) != terminal),
            "删除接口同步清除所有关联管路、渲染对象及待连接状态");
        var reloaded = CdsLayoutComposer.Compose(model.Snapshot, CdsLayout.Parse(model.PrepareSave()));
        check(reloaded.Nodes.All(n => n.Id != terminal) && reloaded.Pipes.Count == 1 && reloaded.Nodes.Count == installed, "删除后的布局可保存和重载，不出现悬空管路或恢复已删接口");
        model.ConnectMode = false; model.Select(tank.Id); await Pump();
        int count = model.Components.Count; model.DeleteSelectionCommand.Execute(null);
        check(!editor.DeleteSelectionButton.IsEnabled && model.Components.Count == count && !editor.TryDeleteSelection(Key.Delete, ModifierKeys.None, editor.Scene.Stage),
            "布局删除入口不能删除来自装机配置的Tank或其他硬件");
        model.ConnectMode = true; model.Connect(tank.Id + ".Inlet"); model.Connect(valve.Id + ".Outlet"); model.ConnectMode = false;
        string extra = model.Layout.Pipes.Single(p => p.Id != pipeId).Id; model.SelectPipe(extra); await Pump(); await Click(editor.DeleteSelectionButton);
        check(model.Layout.Pipes.Count == 1 && model.Layout.Pipes[0].Id == pipeId && model.Components.Count == installed && model.Layout.ToXml() == original,
            "同一删除入口也能单独删除管路，保留装机硬件和其他布局");
        model.SelectedInspectorTab = 0; model.Select(tank.Id);
    }
}
