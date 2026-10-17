using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using xyz.Client.Presentation.Controls.ThreeD;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;

namespace CdsLayoutPreview;

/// <summary>独立布局编辑器：复用原生模型，只编辑布局和模拟显示，不连接后端。</summary>
public partial class PreviewPage : UserControl
{
    // 与框架 FluidPipe 的约定一致：100 个画布单位对应 1 个管路模型单位。
    internal const double PipeSceneScale = 0.01;
    private readonly Dictionary<string, TankVisual3D> _bodies = [];
    private readonly Dictionary<string, FluidRouteVisual3D> _pipes = [];
    private readonly LayoutHistory _history;
    private bool _ready;
    private bool _updatingProperties;
    private string _tool = "select";
    private string _mode = "supply";
    private string? _selection;
    private int _bend = -1;
    private string? _dragSnapshot;
    private Point _dragStart;
    private Point _nodeStart;
    private string? _drawFrom;
    private readonly List<Point> _draft = [];
    private Point _pointer;
    private string? _filePath;
    private (double Width, double Height, bool Editing)? _gridStamp;
    private readonly Dictionary<string, PipeAppearance> _appliedAppearance = [];
    private readonly Dictionary<string, Rect> _pipeTextBounds = [];

    public LayoutEditorViewModel ViewModel { get; }
    public TankModel Tank => ViewModel.Tank;
    public LayoutDocument Document => _history.Document;
    internal bool IsDirty => _history.IsDirty;
    internal bool HasDraft => _drawFrom is not null;
    internal int PathUpdateCount { get; private set; }
    private LayoutNode? SelectedNode => Document.Nodes.FirstOrDefault(node => node.Id == _selection);
    private LayoutPipe? SelectedPipe => Document.Pipes.FirstOrDefault(pipe => pipe.Id == _selection);

    public PreviewPage() : this(LayoutDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "CdsLayout.xml")))
    {
    }

    public PreviewPage(LayoutDocument document, ResourceDictionary? theme = null)
    {
        InitializeComponent();
        if (theme is not null)
        {
            Resources.MergedDictionaries.Add(theme);
        }
        ViewModel = new LayoutEditorViewModel(document, () => ApplyPipeProperties());
        _history = ViewModel.History;
        DataContext = ViewModel;
        _ready = true;
        Refresh();
        LevelPicker.SelectedIndex = 2;
        Refresh();
        ObjectPicker.SelectedItem = Document.Pipes.FirstOrDefault();
        Status("拖动槽和外部接口调整位置；点击“画管路”开始连接。布局修改不影响实际设备。");
    }

    private void Refresh(bool rebuildList = true)
    {
        Stage.Width = Document.Width;
        Stage.Height = Document.Height;
        var camera = (OrthographicCamera)Viewport.Camera;
        camera.Position = new Point3D(Document.Width / 2, -Document.Height / 2, 2000);
        camera.Width = Document.Width;
        foreach (string id in _bodies.Keys.Except(Document.Nodes.Where(node => node.Kind == "Tank").Select(node => node.Id)).ToArray())
        {
            Viewport.Children.Remove(_bodies[id]);
            _bodies.Remove(id);
        }
        foreach (var tank in Document.Nodes.Where(node => node.Kind == "Tank"))
        {
            if (!_bodies.TryGetValue(tank.Id, out var body))
            {
                body = new TankVisual3D { BodyColor = ColorToken("DarkHardwareTankRimColor"), WallTransparency = 0.72, FillFraction = 0.47 };
                _bodies.Add(tank.Id, body);
                Viewport.Children.Add(body);
            }
            body.Transform = tank.TankTransform();
        }
        foreach (string id in _pipes.Keys.Except(Document.Pipes.Select(pipe => pipe.Id)).ToArray())
        {
            _pipes[id].IsAnimationEnabled = false;
            Viewport.Children.Remove(_pipes[id]);
            _pipes.Remove(id);
            _appliedAppearance.Remove(id);
        }
        foreach (var model in Document.Pipes)
        {
            if (!_pipes.TryGetValue(model.Id, out var visual))
            {
                visual = new FluidRouteVisual3D
                {
                    Transform = new ScaleTransform3D(1 / PipeSceneScale, 1 / PipeSceneScale, 1 / PipeSceneScale)
                };
                _pipes.Add(model.Id, visual);
                Viewport.Children.Insert(Viewport.Children.Count - 1, visual);
            }
            if (!Viewport.Children.Contains(visual))
            {
                Viewport.Children.Insert(Viewport.Children.Count - 1, visual);
            }
            ApplyAppearance(model, visual);
            var path = new Point3DCollection { ScalePipePoint(Document.Port3D(model.From)) };
            foreach (var point in model.Bends)
            {
                path.Add(ScalePipePoint(new Point3D(point.X, -point.Y, 0)));
            }
            path.Add(ScalePipePoint(Document.Port3D(model.To)));
            if (visual.Path is null || !visual.Path.SequenceEqual(path))
            {
                path.Freeze();
                visual.Path = path;
                PathUpdateCount++;
            }
            visual.IsFlowing = model.Mode == _mode;
        }
        DrawGrid();
        DrawOverlay();
        UpdateProperties(rebuildList);
        SetMode(_mode);
    }

    private void DrawGrid()
    {
        var stamp = (Document.Width, Document.Height, _tool != "preview");
        if (_gridStamp == stamp)
        {
            return;
        }
        _gridStamp = stamp;
        GridLayer.Children.Clear();
        if (_tool == "preview")
        {
            return;
        }
        for (double x = 0; x <= Document.Width; x += 50)
        {
            GridLayer.Children.Add(new Line { X1 = x, X2 = x, Y2 = Document.Height, Stroke = BrushToken("DarkBorderBrush"), Opacity = 0.28, StrokeThickness = 0.6 });
        }
        for (double y = 0; y <= Document.Height; y += 50)
        {
            GridLayer.Children.Add(new Line { Y1 = y, Y2 = y, X2 = Document.Width, Stroke = BrushToken("DarkBorderBrush"), Opacity = 0.28, StrokeThickness = 0.6 });
        }
    }

    private void DrawOverlay()
    {
        Overlay.Children.Clear();
        _pipeTextBounds.Clear();
        foreach (var node in Document.Nodes)
        {
            if (node.Kind == "Tank")
            {
                AddText(node.Text == node.Id ? node.Text : node.Text + " / " + node.Id, node.X + 22, node.Y - 55, 20);
            }
        }
        foreach (var model in Document.Pipes)
        {
            if (string.IsNullOrWhiteSpace(model.Text))
            {
                continue;
            }
            var label = new TextBlock
            {
                Text = model.Text, FontSize = 14, MaxWidth = Math.Min(280, Document.Width - 16),
                TextWrapping = TextWrapping.Wrap, Foreground = BrushToken("DarkPrimaryText"),
                Background = BrushToken("DarkSurfaceBackground"), Padding = new Thickness(3, 1, 3, 1), Tag = model.Id
            };
            label.Measure(new Size(label.MaxWidth, double.PositiveInfinity));
            Point position = Document.PipeTextPosition(model);
            position.X = Math.Clamp(position.X, 8, Math.Max(8, Document.Width - label.DesiredSize.Width - 8));
            position.Y = Math.Clamp(position.Y, 8, Math.Max(8, Document.Height - label.DesiredSize.Height - 8));
            Put(label, position.X, position.Y);
            _pipeTextBounds[model.Id] = new Rect(position, label.DesiredSize);
        }
        if (_tool == "preview")
        {
            return;
        }
        if (SelectedNode is { } selected)
        {
            Rect bounds = NodeBounds(selected);
            Put(new Rectangle { Width = bounds.Width, Height = bounds.Height, Stroke = BrushToken("DarkAccent"), StrokeThickness = 1.5, StrokeDashArray = [5, 4] }, bounds.Left, bounds.Top);
        }
        if (SelectedPipe is { } pipe)
        {
            Overlay.Children.Add(new Polyline { Points = new PointCollection(Document.Route(pipe)), Stroke = BrushToken("DarkAccent"), StrokeThickness = 2, StrokeDashArray = [4, 3] });
            for (int i = 0; i < pipe.Bends.Count; i++)
            {
                Point point = pipe.Bends[i];
                Put(new Rectangle { Width = 13, Height = 13, Fill = BrushToken(i == _bend ? "DarkAccent" : "DarkSurfaceBackground"), Stroke = BrushToken("DarkAccent"), StrokeThickness = 2 }, point.X - 6.5, point.Y - 6.5);
            }
        }
        foreach (string name in Document.PortNames)
        {
            Point point = Document.Port(name);
            Put(new Ellipse { Width = 16, Height = 16, StrokeThickness = 2, Stroke = BrushToken("DarkAccent"), Fill = BrushToken(name == _drawFrom ? "DarkAccent" : "DarkSurfaceBackground") }, point.X - 8, point.Y - 8);
        }
        if (_drawFrom is not null)
        {
            var points = new List<Point> { Document.Port(_drawFrom) };
            points.AddRange(_draft);
            AppendOrthogonal(points, HitPort(_pointer) is { } port ? Document.Port(port) : Snap(_pointer));
            Overlay.Children.Add(new Polyline { Points = new PointCollection(points), Stroke = BrushToken("DarkAccent"), StrokeThickness = 3, StrokeDashArray = [4, 3] });
            foreach (var point in _draft)
            {
                Put(new Rectangle { Width = 7, Height = 7, Fill = BrushToken("DarkAccent") }, point.X - 3.5, point.Y - 3.5);
            }
        }
    }

    private void UpdateProperties(bool rebuildList)
    {
        _updatingProperties = true;
        if (rebuildList)
        {
            ObjectPicker.ItemsSource = Document.Nodes.Cast<object>().Concat(Document.Pipes).ToList();
        }
        ObjectPicker.SelectedItem = (object?)SelectedNode ?? SelectedPipe;
        var node = SelectedNode;
        var pipe = SelectedPipe;
        ViewModel.PipeProperties.Load(pipe);
        SelectionTitle.Text = node?.Text ?? pipe?.Id ?? "未选择对象";
        SelectionDetails.Text = node is not null
            ? $"{node.Id}\n位置：{node.X:0.#}, {node.Y:0.#}" + (node.Kind == "Tank" ? $"\n{Document.ComponentRoot}.{node.Component}" : "\n外部管路接口")
            : pipe is not null ? $"{pipe.From}\n→ {pipe.To}\n{(_bend >= 0 ? $"选中拐点 {_bend + 1}：{pipe.Bends[_bend].X:0.#}, {pipe.Bends[_bend].Y:0.#}" : $"拐点：{pipe.Bends.Count} 个")}" : "在画布中选择槽、接口或管路。";
        PipeProperties.Visibility = pipe is null ? Visibility.Collapsed : Visibility.Visible;
        PipeProperties.IsEnabled = _tool != "preview";
        switch (pipe?.Mode)
        {
            case "fill":
                PipeModePicker.SelectedIndex = 0;
                break;
            case "supply":
                PipeModePicker.SelectedIndex = 1;
                break;
            case "drain":
                PipeModePicker.SelectedIndex = 2;
                break;
            default:
                PipeModePicker.SelectedIndex = -1;
                break;
        }

        PipeModePicker.IsEnabled = _tool != "preview";
        DeleteButton.IsEnabled = pipe is not null && _tool == "select";
        DeleteButton.Content = _bend >= 0 ? "删除选中拐点" : "删除选中管路";
        UndoButton.IsEnabled = _history.CanUndo && _tool != "preview";
        RedoButton.IsEnabled = _history.CanRedo && _tool != "preview";
        FileText.Text = (_filePath ?? "示例布局 · 保存时选择文件位置") + (_history.IsDirty ? "  ● 未保存" : "");
        FileText.ToolTip = _filePath;
        SelectButton.Background = BrushToken(_tool == "select" ? "DarkAccentPressed" : "DarkControlBackground");
        DrawButton.Background = BrushToken(_tool == "draw" ? "DarkAccentPressed" : "DarkControlBackground");
        PreviewButton.Background = BrushToken(_tool == "preview" ? "DarkAccentPressed" : "DarkControlBackground");
        _updatingProperties = false;
    }

    private void OnObjectSelected(object sender, SelectionChangedEventArgs args)
    {
        if (_updatingProperties || !_ready)
        {
            return;
        }
        object? requested = ObjectPicker.SelectedItem;
        if (!ApplyPipeProperties())
        {
            UpdateProperties(false);
            return;
        }
        switch (requested)
        {
            case LayoutNode node:
                _selection = node.Id;
                break;
            case LayoutPipe pipe:
                _selection = pipe.Id;
                break;
            default:
                _selection = null;
                break;
        }

        _bend = -1;
        DrawOverlay();
        UpdateProperties(false);
    }

    private void OnPipeModeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_updatingProperties || !_ready || SelectedPipe is not { } pipe || PipeModePicker.SelectedItem is not ComboBoxItem item)
        {
            return;
        }
        string before = Document.ToXml();
        pipe.Mode = (string)item.Tag;
        _history.Commit(before);
        Refresh(false);
    }

    public bool ApplyPipeProperties()
    {
        if (SelectedPipe is not { } pipe || !ViewModel.PipeProperties.HasPendingChanges)
        {
            return true;
        }
        if (!ViewModel.PipeProperties.TryRead(out var appearance, out string medium))
        {
            Status(ViewModel.PipeProperties.Error);
            return false;
        }
        string before = Document.ToXml();
        pipe.Appearance = appearance;
        pipe.Medium = medium;
        pipe.Text = ViewModel.PipeProperties.Text.Trim();
        _history.Commit(before);
        ViewModel.PipeProperties.Load(pipe, force: true);
        Refresh(false);
        Status("管路属性已应用；保存布局时会一并写入 XML。");
        return true;
    }

    private static Point3D ScalePipePoint(Point3D point)
    {
        return new Point3D(point.X * PipeSceneScale, point.Y * PipeSceneScale, point.Z * PipeSceneScale);
    }

    private void ApplyAppearance(LayoutPipe model, FluidRouteVisual3D visual)
    {
        var appearance = model.Appearance;
        if (_appliedAppearance.TryGetValue(model.Id, out var before) && before == appearance)
        {
            return;
        }
        // 清空路径后批量设几何参数，避免每设一个参数就对旧路径重新生成网格。
        if (before is null || before.Diameter != appearance.Diameter || before.WallThickness != appearance.WallThickness || before.BendRadius != appearance.BendRadius)
        {
            visual.Path = null;
            visual.PipeRadius = appearance.Diameter / 2 * PipeSceneScale;
            visual.WallThickness = appearance.WallThickness * PipeSceneScale;
            visual.BendRadius = appearance.BendRadius * PipeSceneScale;
        }
        visual.WallOpacity = appearance.WallOpacity;
        visual.WallColor = appearance.WallColor;
        visual.FlowColor = appearance.FlowColor;
        visual.FlowSpeed = appearance.FlowSpeed * PipeSceneScale;
        visual.ReverseFlow = appearance.ReverseFlow;
        visual.IsFilled = appearance.IsFilled;
        visual.IsAnimationEnabled = appearance.IsAnimationEnabled;
        _appliedAppearance[model.Id] = appearance;
    }

    public void SetMode(string mode)
    {
        _mode = mode;
        switch (mode)
        {
            case "fill":
                ModeText.Text = "模拟 · 补液中";
                break;
            case "supply":
                ModeText.Text = "模拟 · 供液中";
                break;
            case "drain":
                ModeText.Text = "模拟 · 排液中";
                break;
            case "idle":
                ModeText.Text = "模拟 · 已停止";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }

        foreach (var pipe in Document.Pipes)
        {
            _pipes[pipe.Id].IsFlowing = pipe.Mode == mode;
        }
        foreach (var button in new[] { FillButton, SupplyButton, DrainButton, StopButton })
        {
            button.Background = BrushToken((string)button.Tag == mode ? "DarkAccentPressed" : "DarkControlBackground");
        }
    }

    private void OnModeClick(object sender, RoutedEventArgs args) => SetMode((string)((Button)sender).Tag);

    private void OnToggleProperties(object sender, RoutedEventArgs args)
    {
        bool show = PropertiesPanel.Visibility != Visibility.Visible;
        PropertiesPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PropertiesColumn.Width = new GridLength(show ? 270 : 0);
        PropertiesToggle.Content = show ? "收起属性" : "展开属性";
    }

    private void OnLevelChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_ready || LevelPicker.SelectedIndex < 0)
        {
            return;
        }
        int index = LevelPicker.SelectedIndex;
        Tank.Update(new TankDto
        {
            Path = Document.ComponentRoot + "." + Document.Nodes.First(node => node.Kind == "Tank").Component, Level = index + 1,
            LL = Signal("LL", index >= 1), L = Signal("L", index >= 2), H = Signal("H", index >= 3),
            HH = Signal("HH", index >= 4), Overfill = Signal("Overfill", index >= 5)
        });
        double[] fractions = [0.05, 0.205, 0.47, 0.72, 0.865, 0.998];
        foreach (var body in _bodies.Values)
        {
            double previous = body.FillFraction;
            body.BeginAnimation(TankVisual3D.FillFractionProperty, null);
            body.FillFraction = fractions[index];
            body.IsAlarm = index >= 4;
            body.BeginAnimation(TankVisual3D.FillFractionProperty, new DoubleAnimation(previous, fractions[index], TimeSpan.FromMilliseconds(200)) { FillBehavior = FillBehavior.Stop });
        }
    }

    public void PauseAnimation()
    {
        foreach (var pipe in _pipes.Values)
        {
            pipe.IsAnimationEnabled = false;
        }
    }

    private static DiSensorDto Signal(string name, bool active) => new() { Name = name, IsActive = active };
    private Brush BrushToken(string key) => (Brush)FindResource(key);
    private Color ColorToken(string key) => (Color)FindResource(key);
    private void Status(string message) => StatusText.Text = message;

    private void AddText(string text, double x, double y, double size)
    {
        Put(new TextBlock { Text = text, FontSize = size, Foreground = BrushToken("DarkPrimaryText") }, x, y);
    }

    private void Put(UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        Overlay.Children.Add(element);
    }
}
