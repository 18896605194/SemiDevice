using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using CdsAssemblyPreview;

namespace ToolBuilder;

internal static partial class ToolBuilderSmoke
{
    private static async Task HistoryChecks(MainViewModel host, CdsLayoutViewModel session, string machine, Action<bool, string> check, Func<int, Task> pump, Func<string, Task> screenshot)
    {
        var editor = session.Editor; var model = editor.ViewModel;
        string original = model.Layout.ToXml(); var snapshot = model.Snapshot;
        var tank = model.Components.First(n => n.Kind == "Tank");
        var valve = model.Components.First(n => n.Kind == "Valve");
        string pipeId = model.Layout.Pipes[0].Id;
        void Baseline(string? xml = null)
        {
            editor.Scene.PointerUp(); model.LoadConfiguration(snapshot, CdsLayout.Parse(xml ?? original)); model.SelectedInspectorTab = 0;
        }
        async Task Click(Button button) { ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); await pump(80); }
        void Undo() => model.UndoCommand.Execute(null);
        void Redo() => model.RedoCommand.Execute(null);

        Baseline(); await pump(80);
        check(!model.UndoCommand.CanExecute(null) && !model.RedoCommand.CanExecute(null) && !editor.UndoButton.IsEnabled && !editor.RedoButton.IsEnabled,
            "CDS加载后的初始布局不占撤销步骤，按钮正确禁用");
        model.Select(valve.Id); model.OpenValveCommand.Execute(null); model.SelectedInspectorTab = 2;
        model.Select(tank.Id); model.SelectedLevel = 4;
        check(!model.UndoCommand.CanExecute(null), "选择、切换页签和模拟动作不进入布局历史");
        model.SelectedInspectorTab = 0; model.PositionX += 1; await pump(80);
        check(editor.UndoButton.IsEnabled && session.IsDirty && host.Title.EndsWith("*"), "坐标编辑启用撤销并更新宿主修改标记");
        string moved = model.Layout.ToXml();
        check(!editor.TryUndoRedo(Key.Z, ModifierKeys.Control, editor.PositionXInput) && model.Layout.ToXml() == moved,
            "输入框Ctrl+Z保留文本自身的撤销，不误操作整个布局");
        await Click(editor.UndoButton);
        check(model.Layout.ToXml() == original && !session.IsDirty && !host.Title.EndsWith("*") && editor.RedoButton.IsEnabled,
            "撤销按钮恢复已保存坐标及关联管路，修改标记同步清除");
        check(model.SelectedLevel == 4 && model.ValveStates[snapshot.ComponentRoot + "." + valve.Component] == true,
            "撤销布局不回退液位及阀门的模拟反馈");
        check(editor.TryUndoRedo(Key.Y, ModifierKeys.Control, editor.Scene.Stage) && model.Layout.ToXml() == moved, "画布Ctrl+Y重做完整布局编辑");
        editor.ShowXaml(true); await pump(80);
        check(editor.TryUndoRedo(Key.Z, ModifierKeys.Control, editor.XamlPreview) && CdsDiagramXaml.ReadLayout(editor.XamlPreview.Text).ToXml() == original,
            "查看只读XAML时Ctrl+Z撤销布局且源码同步更新");
        check(editor.TryUndoRedo(Key.Z, ModifierKeys.Control | ModifierKeys.Shift, editor.XamlPreview) && model.Layout.ToXml() == moved,
            "Ctrl+Shift+Z支持重做布局");
        editor.ShowXaml(false); model.SelectedInspectorTab = 0; await pump(80); await Click(editor.UndoButton);
        model.PositionX += 2;
        check(!model.RedoCommand.CanExecute(null), "撤销后执行新的布局编辑会清除旧重做分支");

        Baseline(); model.Select(tank.Id); await pump(80);
        var body = model.Layout.Nodes.Single(n => n.Id == tank.Id); var start = new Point(body.X + body.Width * .3, body.Y + body.Height * .5);
        editor.Scene.PointerDown(start);
        editor.Scene.PointerMove(start + new Vector(20, 10)); editor.Scene.PointerMove(start + new Vector(40, 20)); editor.Scene.PointerMove(start + new Vector(60, 30)); editor.Scene.PointerUp();
        string dragged = model.Layout.ToXml(); check(dragged != original, "真实鼠标拖动产生布局变化");
        Undo(); check(model.Layout.ToXml() == original && !model.UndoCommand.CanExecute(null), "多次鼠标移动合并为一次撤销，恢复组件和管路拐点");
        Redo(); check(model.Layout.ToXml() == dragged, "一次重做恢复拖动最终位置");
        Baseline(); model.Select(tank.Id); editor.Scene.PointerDown(start); editor.Scene.PointerMove(start + new Vector(30, 30));
        editor.Scene.Stage.ReleaseMouseCapture();
        Undo(); check(model.Layout.ToXml() == original && !model.UndoCommand.CanExecute(null), "丢失鼠标捕获时结束拖动事务，仍可一次撤销");
        Baseline(); model.Select(tank.Id); editor.Scene.PointerDown(start); editor.Scene.PointerMove(start + new Vector(30, 30));
        check(editor.TryUndoRedo(Key.Z, ModifierKeys.Control, editor.Scene.Stage) && model.Layout.ToXml() == original && !editor.Scene.Stage.IsMouseCaptured,
            "拖动过程中Ctrl+Z先结束手势再撤销，不留下未完成历史");
        Baseline(); model.Select(tank.Id); editor.Scene.PointerDown(start); editor.Scene.PointerUp();
        check(!model.UndoCommand.CanExecute(null), "单击选择不产生空撤销步骤");
        editor.TryMoveSelection(Key.Right, ModifierKeys.None, editor.Scene.Stage); Undo();
        check(model.Layout.ToXml() == original && !model.UndoCommand.CanExecute(null), "方向键微调可独立撤销");

        var bends = CdsLayout.Parse(original); bends.Pipes[0] = bends.Pipes[0] with { Bends = [new Point(50, 60), new Point(50, 200)] };
        string bent = bends.ToXml(); Baseline(bent); model.SelectPipe(pipeId); await pump(80);
        editor.Scene.PointerDown(new Point(50, 60)); editor.Scene.PointerMove(new Point(70, 80)); editor.Scene.PointerMove(new Point(90, 110)); editor.Scene.PointerUp();
        check(model.Layout.Pipes[0].Bends[0] == new Point(90, 110), "拐点鼠标拖动作用于选中管路");
        Undo(); check(model.Layout.ToXml() == bent && !model.UndoCommand.CanExecute(null), "管路拐点多次拖动合并为一步撤销");
        model.BeginGesture(); model.MovePipe(pipeId, model.PipeCenter(model.Layout.Pipes[0]) + new Vector(20, 20));
        model.MovePipe(pipeId, model.PipeCenter(model.Layout.Pipes[0]) + new Vector(20, 20)); model.EndGesture();
        Undo(); check(model.Layout.ToXml() == bent && !model.UndoCommand.CanExecute(null), "整段管路拖动也合并为一步撤销");

        Baseline(); model.SelectPipe(pipeId);
        model.Parameters.OfType<CdsNumberParameter>().Single(p => p.Key == "Diameter").Value = 2;
        check(model.Layout.Pipes[0].Diameter == 2 && model.Layout.Pipes[0].WallThickness < 1, "缩小管径同步约束管壁厚度");
        Undo(); check(model.Layout.ToXml() == original && !model.UndoCommand.CanExecute(null), "管径和联动管壁作为一次参数修改整体恢复");
        Baseline(); model.Select(tank.Id); model.Parameters.OfType<CdsTextParameter>().Single(p => p.Key == "Caption").Value = "Undo caption";
        Undo(); check(model.Layout.ToXml() == original, "组件文字修改支持撤销");
        Baseline(); model.SelectPipe(pipeId); model.Parameters.OfType<CdsTextParameter>().Single(p => p.Key == "FlowColor").Value = "not-a-color";
        check(!model.UndoCommand.CanExecute(null) && model.Layout.ToXml() == original, "非法颜色草稿不进入布局历史");

        Baseline(); model.AddInterfaceCommand.Execute(null); string terminalId = model.Selected!.Id;
        Undo(); check(model.Layout.ToXml() == original && !model.UndoCommand.CanExecute(null), "新增外部接口可撤销");
        Redo(); check(model.Selected?.Id == terminalId && model.Components.Any(n => n.Id == terminalId), "重做接口保留原ID及选中对象");
        model.ConnectMode = true; model.Connect(terminalId + ".Port"); model.Connect(tank.Id + ".Inlet");
        model.Connect(terminalId + ".Port"); model.Connect(valve.Id + ".Inlet"); model.ConnectMode = false;
        string connected = model.Layout.ToXml(); model.Select(terminalId); model.DeleteSelectionCommand.Execute(null); Undo(); await pump(80);
        check(model.Layout.ToXml() == connected && model.Selected?.Id == terminalId && editor.Scene.Pipes.Count == 3,
            "删除接口的一次撤销同时恢复接口、全部关联管路及选中对象");
        Redo(); check(model.Layout.ToXml() == original && editor.Scene.Pipes.Count == 1, "重做接口删除不会留下悬空管路");
        Baseline(); model.ConnectMode = true; model.Connect(tank.Id + ".Inlet"); model.Connect(valve.Id + ".Outlet"); model.ConnectMode = false;
        string connection = model.Layout.ToXml(); Undo(); check(model.Layout.ToXml() == original && !model.UndoCommand.CanExecute(null), "新增管路可完整撤销");
        Redo(); check(model.Layout.ToXml() == connection, "重做连接保留管段ID、端口及拐点");
        Baseline(); model.SelectPipe(pipeId); model.DeleteSelectionCommand.Execute(null); Undo();
        check(model.Layout.ToXml() == original && model.SelectedPipeId == pipeId, "单独删除管路可撤销并恢复选择");
        Baseline(); model.ClearPipesCommand.Execute(null); Undo(); check(model.Layout.ToXml() == original && !model.UndoCommand.CanExecute(null), "清空管路可一步恢复");
        Baseline(); model.ResetCommand.Execute(null); Undo(); check(model.Layout.ToXml() == original && !model.UndoCommand.CanExecute(null), "重置整个布局可一步恢复");
        Baseline(); var imported = CdsLayout.Parse(original); imported.Nodes[0] = imported.Nodes[0] with { Text = "Imported caption" };
        editor.OpenXaml(CdsDiagramXaml.Export(imported)); Undo(); check(model.Layout.ToXml() == original && !model.UndoCommand.CanExecute(null), "打开XAML作为一次布局操作可撤销");

        Baseline(); model.Select(tank.Id); model.PositionX += 1; host.SaveWorkspace(machine);
        check(!session.IsDirty && model.UndoCommand.CanExecute(null), "顶部保存清除修改标记但保留撤销历史");
        Undo(); check(model.Layout.ToXml() == original && session.IsDirty && host.Title.EndsWith("*"), "撤销已保存编辑后重新标记未保存");
        Redo(); check(!session.IsDirty && !host.Title.EndsWith("*"), "重做到保存点正确清除修改标记");
        Undo(); host.SaveWorkspace(machine); Baseline();
        model.Select(tank.Id); model.PositionX += 1; model.LoadConfiguration(snapshot, CdsLayout.Parse(original));
        check(!model.UndoCommand.CanExecute(null) && !model.RedoCommand.CanExecute(null), "装机清单重新协调时建立新历史基线，布局撤销不回退装机配置");
        model.Select(tank.Id);
        for (int i = 0; i < 105; i++) model.PositionX += .1;
        int undone = 0; while (model.UndoCommand.CanExecute(null)) { Undo(); undone++; }
        check(undone == 100, "每个CDS会话最多保留100步撤销，限制长期编辑内存占用");
        Baseline(); model.Select(tank.Id); model.PositionX += 1; await screenshot("toolbuilder-cds-undo.png");
        Undo(); check(model.Layout.ToXml() == original && !session.IsDirty, "撤销验证结束恢复已保存布局，不残留测试修改");
    }
}
