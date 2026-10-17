using System.IO;
using System.Windows;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Controls.ThreeD;

namespace CdsLayoutPreview;

internal static class EditorSmoke
{
    public static void Run(PreviewPage page, string output)
    {
        var results = new List<string>();
        void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
            results.Add("PASS " + message);
        }

        string initial = page.Document.ToXml();
        Check(page.Viewport.Children.OfType<TankVisual3D>().Count() == 1
            && page.Viewport.Children.OfType<FluidRouteVisual3D>().Count() == 3, "复用一个原生槽和三条原生管路");
        Point oldPort = page.Document.Port("Tank1.Inlet");
        page.PointerDown(new Point(500, 280));
        int dragPaths = page.PathUpdateCount;
        page.PointerMove(new Point(580, 310));
        Check(page.PathUpdateCount == dragPaths, "拖动槽期间不生成三维管路网格");
        page.PointerUp();
        var tank = page.Document.Nodes.Single(node => node.Kind == "Tank");
        Check(tank.X == 480 && tank.Y == 190 && page.IsDirty, "拖动槽，位置按画布坐标更新并标为未保存");
        Check((page.Document.Port("Tank1.Inlet") - oldPort - new Vector(80, 30)).Length < 0.001, "槽移动后连接端跟随");
        var firstPipe = page.Viewport.Children.OfType<FluidRouteVisual3D>().First();
        Check((firstPipe.Transform.Transform(firstPipe.Path![^1]) - page.Document.Port3D("Tank1.Inlet")).Length < 0.0001, "原生管路模型的终点同步更新");
        string moved = page.Document.ToXml();
        page.Undo();
        Check(page.Document.ToXml() == initial && !page.IsDirty, "一次撤销还原整个拖动及关联拐点");
        page.Redo();
        Check(page.Document.ToXml() == moved, "重做还原槽与管路位置");
        page.PointerDown(new Point(550, 280));
        page.PointerMove(new Point(650, 380));
        page.CancelGesture();
        Check(page.Document.ToXml() == moved, "取消拖动不留下坐标修改");
        page.Undo();

        page.PointerDown(new Point(60, 180));
        page.PointerMove(new Point(90, 150));
        page.PointerUp();
        Check(page.Document.Port("FillSource.Port") == new Point(90, 150), "外部接口可拖动");
        page.Undo();

        page.PointerDown(new Point(180, 180), 2);
        var fill = page.Document.Pipes.Single(pipe => pipe.Id == "FillPipe");
        Check(fill.Bends.Count == 3, "双击管线插入拐点");
        page.PointerDown(new Point(180, 180));
        dragPaths = page.PathUpdateCount;
        page.PointerMove(new Point(220, 140));
        Check(page.PathUpdateCount == dragPaths, "拖动拐点期间不生成三维管路网格");
        page.PointerUp();
        Check(page.PathUpdateCount - dragPaths == 1, "松开拐点后仅更新受影响的一条管路");
        Check(fill.Bends[0] == new Point(220, 140), "拖动选中拐点改变线路");
        page.DeleteSelection();
        Check(fill.Bends.Count == 2, "删除单个拐点");
        page.Undo();
        Check(page.Document.Pipes.Single(pipe => pipe.Id == "FillPipe").Bends.Count == 3, "撤销恢复被删除的拐点");

        page.ObjectPicker.SelectedItem = page.Document.Pipes.Single(pipe => pipe.Id == "FillPipe");
        page.DeleteSelection();
        Check(page.Document.Pipes.Count == 2, "通过对象选择器选择并删除整条管路");
        page.SetTool("draw");
        page.PointerDown(page.Document.Port("FillSource.Port"));
        page.PointerDown(new Point(200, 100));
        page.PointerDown(new Point(350, 100));
        page.PointerDown(new Point(350, 220));
        page.PointerDown(page.Document.Port("Tank1.Inlet"));
        var created = page.Document.Pipes.Single(pipe => pipe.Id.StartsWith("Pipe"));
        Check(page.Document.Pipes.Count == 3 && !page.HasDraft && created.From == "FillSource.Port" && created.To == "Tank1.Inlet", "直接点选起点、拐点和终点创建管路");
        var route = page.Document.Route(created);
        Check(route.Zip(route.Skip(1)).All(pair => Math.Abs(pair.First.X - pair.Second.X) < 1 || Math.Abs(pair.First.Y - pair.Second.Y) < 1), "绘制时自动生成正交折线");
        page.PipeModePicker.SelectedIndex = 2;
        Check(created.Mode == "drain", "属性面板修改模拟状态绑定");
        page.PipeModePicker.SelectedIndex = 0;
        var properties = page.ViewModel.PipeProperties;
        properties.Diameter = "24";
        properties.WallThickness = "2";
        properties.BendRadius = "28";
        properties.WallOpacity = "0.4";
        properties.FlowColor = "#FFB74D";
        properties.WallColor = "#D0D0D0";
        properties.FlowSpeed = "90";
        properties.Medium = "DIW";
        properties.ReverseFlow = true;
        properties.IsAnimationEnabled = false;
        properties.ApplyCommand.Execute(null);
        Check(created.Appearance.Diameter == 24 && created.Appearance.WallThickness == 2 && created.Appearance.BendRadius == 28
            && created.Appearance.WallOpacity == 0.4 && created.Appearance.FlowSpeed == 90 && created.Appearance.ReverseFlow
            && !created.Appearance.IsAnimationEnabled && created.Medium == "DIW", "属性 ViewModel 应用几何、外观、介质和动画配置");
        Check(page.Viewport.Children.OfType<FluidRouteVisual3D>().Any(visual => Math.Abs(visual.PipeRadius - 0.12) < 0.00001
            && visual.FlowColor == PipeAppearance.ParseColor("#FFB74D") && visual.ReverseFlow && !visual.IsAnimationEnabled), "属性传入框架原生管路控件并使用正确场景比例");
        var applied = created.Appearance;
        properties.WallThickness = "30";
        Check(!page.ApplyPipeProperties() && created.Appearance == applied && properties.Error.Length > 0, "非法管壁厚度不写入布局");
        properties.WallThickness = "2";
        properties.FlowSpeed = "NaN";
        Check(!page.ApplyPipeProperties() && created.Appearance == applied, "非有限动画速度被拒绝");
        properties.FlowSpeed = "90";
        properties.FlowColor = "not-a-color";
        Check(!page.ApplyPipeProperties() && created.Appearance == applied, "非法颜色被拒绝");
        properties.FlowColor = "#FFB74D";
        Check(page.ApplyPipeProperties(), "修正属性后可以正常应用");
        page.Undo();
        Check(page.Document.Pipes.Single(pipe => pipe.Id == created.Id).Appearance.Diameter == 16, "撤销恢复整组管路属性");
        page.Redo();
        created = page.Document.Pipes.Single(pipe => pipe.Id == created.Id);
        Check(created.Appearance == applied, "重做恢复整组管路属性");
        string edited = page.Document.ToXml();

        page.SetTool("draw");
        page.PointerDown(page.Document.Port("FillSource.Port"));
        page.PointerDown(new Point(100, 70));
        page.CancelGesture();
        Check(!page.HasDraft && page.Document.ToXml() == edited, "取消未完成的管路不修改已完成布局");
        page.SetTool("preview");
        page.PointerDown(new Point(500, 280));
        page.PointerMove(new Point(700, 380));
        page.PointerUp();
        Check(page.Document.ToXml() == edited && page.Overlay.Children.OfType<System.Windows.Shapes.Ellipse>().Count() == 0, "运行预览隐藏编辑手柄并锁定拖动");
        page.SetTool("select");
        string savedPath = Path.Combine(output, "editor-roundtrip.xml");
        page.SaveTo(savedPath);
        Check(!page.IsDirty && LayoutDocument.Load(savedPath).ToXml() == edited, "保存并重新解析后坐标、连接、拐点和状态绑定完整一致");
        page.PointerDown(new Point(500, 280));
        page.PointerMove(new Point(540, 320));
        page.PointerUp();
        page.LoadFrom(savedPath);
        Check(page.Document.ToXml() == edited && !page.IsDirty, "加载已保存文件恢复画面且清除未保存标记");
        string invalidPath = Path.Combine(output, "editor-invalid.xml");
        File.WriteAllText(invalidPath, edited.Replace("Tank1.Inlet", "Missing.Inlet"));
        bool rejected = false;
        try
        {
            page.LoadFrom(invalidPath);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }
        Check(rejected && page.Document.ToXml() == edited, "无效连接口加载被拒且当前布局保留");
        rejected = false;
        try
        {
            page.SaveTo(output);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            rejected = true;
        }
        Check(rejected && File.ReadAllText(savedPath) == edited, "写入失败不破坏已保存布局");
        foreach (string invalid in new[]
        {
            edited.Replace("Version=\"1\"", "Version=\"2\""),
            edited.Replace("Width=\"1000\"", "Width=\"NaN\""),
            edited.Replace("Id=\"Tank1\"", "Id=\"FillSource\"")
        })
        {
            rejected = false;
            try
            {
                LayoutDocument.Parse(invalid);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            Check(rejected, "拒绝不支持的版本、非有限尺寸或重复 ID");
        }

        page.ObjectPicker.SelectedItem = page.Document.Pipes.Single(pipe => pipe.Id == created.Id);
        page.ViewModel.PipeProperties.FlowSpeed = "120";
        string pendingPath = Path.Combine(output, "editor-pending-properties.xml");
        page.SaveTo(pendingPath);
        Check(LayoutDocument.Load(pendingPath).Pipes.Single(pipe => pipe.Id == created.Id).Appearance.FlowSpeed == 120
            && !page.ViewModel.PipeProperties.HasPendingChanges, "直接保存会应用并写入尚未点击应用的有效属性");
        page.LoadFrom(savedPath);
        page.ObjectPicker.SelectedItem = page.Document.Pipes.Single(pipe => pipe.Id == created.Id);
        var labelPipe = page.Document.Pipes.Single(pipe => pipe.Id == "SupplyPipe");
        page.ObjectPicker.SelectedItem = labelPipe;
        int labelPaths = page.PathUpdateCount;
        page.PipeTextEditor.Text = "DIW 供液 <A&B>";
        Check(page.ApplyPipeProperties() && labelPipe.Text == "DIW 供液 <A&B>" && page.PathUpdateCount == labelPaths,
            "文字编辑绑定并应用，不重建三维管路");
        System.Windows.Controls.TextBlock Label() => page.Overlay.Children.OfType<System.Windows.Controls.TextBlock>()
            .Single(item => Equals(item.Tag, "SupplyPipe"));
        Point LabelLocation() => new(System.Windows.Controls.Canvas.GetLeft(Label()), System.Windows.Controls.Canvas.GetTop(Label()));
        Point labelBefore = LabelLocation();
        page.PointerDown(new Point(500, 280));
        page.PointerMove(new Point(540, 320));
        Check(LabelLocation() != labelBefore, "拖动槽的过程中管路文字实时跟随路径");
        page.PointerUp();
        page.Undo();
        Check(LabelLocation() == labelBefore, "撤销拖动恢复管路文字位置");
        page.PointerDown(labelBefore + new Vector(4, 4));
        Check(page.ObjectPicker.SelectedItem is LayoutPipe { Id: "SupplyPipe" }, "点击管路文字可选中所属管路");
        page.PointerDown(new Point(750, 330), 2);
        var supplyBend = page.Document.Pipes.Single(pipe => pipe.Id == "SupplyPipe").Bends.Single();
        page.PointerDown(supplyBend);
        Point bendLabelBefore = LabelLocation();
        page.PointerMove(supplyBend + new Vector(0, 60));
        Check(LabelLocation() != bendLabelBefore, "拖动拐点的过程中管路文字实时跟随路径");
        page.PointerUp();
        page.Undo();
        page.Undo();
        page.ViewModel.PipeProperties.Text = "";
        Check(page.ApplyPipeProperties() && !page.Overlay.Children.OfType<System.Windows.Controls.TextBlock>().Any(item => Equals(item.Tag, "SupplyPipe")),
            "文字留空隐藏标注");
        page.Undo();
        Check(Label().Text == "DIW 供液 <A&B>", "撤销恢复管路文字");
        page.Redo();
        Check(!page.Overlay.Children.OfType<System.Windows.Controls.TextBlock>().Any(item => Equals(item.Tag, "SupplyPipe")), "重做恢复隐藏文字");
        page.Undo();
        page.SetTool("preview");
        Check(Label().Text == "DIW 供液 <A&B>", "运行预览显示管路文字");
        page.SetTool("select");
        page.ViewModel.PipeProperties.Text = "冲洗供液 <DIW&IPA>";
        string labelPath = Path.Combine(output, "editor-pipe-text.xml");
        page.SaveTo(labelPath);
        page.LoadFrom(labelPath);
        Check(Label().Text == "冲洗供液 <DIW&IPA>", "保存自动应用文字，XML 特殊字符可正确保存加载");
        page.ObjectPicker.SelectedItem = page.Document.Pipes.Single(pipe => pipe.Id == "SupplyPipe");
        File.WriteAllLines(Path.Combine(output, "editor-checks.txt"), results);
    }
}
