using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Localization;

namespace CdsAssemblyPreview;

public sealed partial class PreviewViewModel
{
    #region Column
    public ObservableCollection<CdsPipe> PipeItems { get; } = [];

    private string _selectedPipeId = "";
    public string SelectedPipeId
    {
        get => _selectedPipeId;
        set
        {
            if (!SetProperty(ref _selectedPipeId, value ?? "")) return;
            if (_selectedPipeId.Length > 0) Selected = null;
            SelectionChanged();
        }
    }
    public CdsPipe? SelectedPipe => Layout.Pipes.SingleOrDefault(p => p.Id == SelectedPipeId);
    public bool IsPipeSelected => SelectedPipe is not null;
    public bool IsNodeSelected => Selected is not null;
    public bool HasSelection => IsNodeSelected || IsPipeSelected;
    public bool CanDeleteSelection => IsPipeSelected || CurrentNode?.Kind == "Terminal";
    public double MaxPositionX => CurrentNode is { Kind: not "Terminal" } n ? Layout.Width - n.Width : Layout.Width;
    public double MaxPositionY => CurrentNode is { Kind: not "Terminal" } n ? Layout.Height - n.Height : Layout.Height;
    public double MaxWallThickness => PipeDiameter / 2 - 0.1;

    private double? _verticalGuideX;
    public double? VerticalGuideX { get => _verticalGuideX; private set => SetProperty(ref _verticalGuideX, value); }

    private double _positionX;
    public double PositionX
    {
        get => _positionX;
        set
        {
            if (!double.IsFinite(value) || !SetProperty(ref _positionX, value) || _syncEditors) return;
            if (CurrentNode is { } n) SetNodePosition(Layout.Nodes.IndexOf(n), new Point(value, PositionY));
            else if (SelectedPipe is { } p) MovePipe(p.Id, new Point(value, PositionY));
        }
    }
    private double _positionY;
    public double PositionY
    {
        get => _positionY;
        set
        {
            if (!double.IsFinite(value) || !SetProperty(ref _positionY, value) || _syncEditors) return;
            if (CurrentNode is { } n) SetNodePosition(Layout.Nodes.IndexOf(n), new Point(PositionX, value));
            else if (SelectedPipe is { } p) MovePipe(p.Id, new Point(PositionX, value));
        }
    }
    private double _pipeDiameter = 16;
    public double PipeDiameter
    {
        get => _pipeDiameter;
        set
        {
            if (!double.IsFinite(value) || value is < 2 or > 80 || !SetProperty(ref _pipeDiameter, value) || _syncEditors) return;
            if (SelectedPipe is { } p)
            {
                ReplacePipe(p with { Diameter = value, WallThickness = Math.Min(p.WallThickness, MaxWallThickness) });
                SyncEditors(); OnPropertyChanged(nameof(MaxWallThickness));
            }
        }
    }
    private double _pipeWallThickness = 1.6;
    public double PipeWallThickness
    {
        get => _pipeWallThickness;
        set
        {
            if (!double.IsFinite(value) || value <= 0 || value >= PipeDiameter / 2 || !SetProperty(ref _pipeWallThickness, value) || _syncEditors) return;
            if (SelectedPipe is { } p) ReplacePipe(p with { WallThickness = value });
        }
    }
    #endregion

    #region Service
    private bool _syncEditors;
    #endregion

    private CdsNode? CurrentNode => Selected is null ? null : Layout.Nodes.SingleOrDefault(n => n.Id == Selected.Id);
    public void SelectPipe(string id) => SelectedPipeId = id;

    private void DoSelectMode()
    {
        _startPort = null; ConnectMode = false;
        Status = L10n.Get("cdsp.select.note"); SceneChanged?.Invoke();
    }
    private void SelectionChanged()
    {
        BuildParameters();
        SyncEditors();
        foreach (string property in new[] { nameof(IsTankSelected), nameof(IsValveSelected), nameof(IsNodeSelected), nameof(IsPipeSelected), nameof(HasSelection), nameof(IsSensorSelected), nameof(IsHardwareSelected), nameof(SelectedHardware), nameof(IsHeaterSelected), nameof(HasMotion), nameof(SensorStateText),
            nameof(SelectionPath), nameof(SelectedLevel), nameof(ValveStateText), nameof(CanDeleteSelection), nameof(MaxPositionX), nameof(MaxPositionY), nameof(MaxWallThickness) }) OnPropertyChanged(property);
        OpenValveCommand.NotifyCanExecuteChanged(); CloseValveCommand.NotifyCanExecuteChanged(); UnknownValveCommand.NotifyCanExecuteChanged();
        SensorOnCommand.NotifyCanExecuteChanged(); SensorOffCommand.NotifyCanExecuteChanged(); SensorUnknownCommand.NotifyCanExecuteChanged();
        NudgeCommand.NotifyCanExecuteChanged();
        DeleteSelectionCommand.NotifyCanExecuteChanged();
        SceneChanged?.Invoke();
    }
    private void DoDeleteSelection()
    {
        if (!CanDeleteSelection) return;
        string? terminal = CurrentNode is { Kind: "Terminal" } node ? node.Id : null;
        string? selectedPipe = SelectedPipe?.Id;
        var removedPipes = Layout.Pipes.Where(p => p.Id == selectedPipe || terminal is not null &&
            (CdsLayoutComposer.Owner(p.From) == terminal || CdsLayoutComposer.Owner(p.To) == terminal)).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        Selected = null; SelectedPipeId = "";
        if (terminal is not null && _startPort is not null && CdsLayoutComposer.Owner(_startPort) == terminal) _startPort = null;
        Layout.Pipes.RemoveAll(p => removedPipes.Contains(p.Id));
        foreach (var pipe in PipeItems.Where(p => removedPipes.Contains(p.Id)).ToArray()) PipeItems.Remove(pipe);
        if (terminal is not null)
        {
            Layout.Nodes.RemoveAll(n => n.Id == terminal);
            foreach (var item in Components.Where(n => n.Id == terminal).ToArray()) Components.Remove(item);
        }
        ClearAlignmentGuide(); LayoutSource = L10n.Get("cdsp.layout.edited");
        Status = L10n.Get(terminal is null ? "cdsp.deleted.pipe" : "cdsp.deleted.interface", removedPipes.Count);
        Changed();
    }
    private void DoNudge(Vector delta)
    {
        if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y)) return;
        ClearAlignmentGuide();
        // 方向键精确微调，不经过拖动的网格和吸附，避免一步移动后又被吸回。
        if (CurrentNode is { } node) SetNodePosition(Layout.Nodes.IndexOf(node), new Point(node.X + delta.X, node.Y + delta.Y));
        else if (SelectedPipe is { } pipe) MovePipe(pipe.Id, PipeCenter(pipe) + delta);
    }
    private void SyncEditors()
    {
        _syncEditors = true;
        try
        {
            if (CurrentNode is { } n) { PositionX = n.X; PositionY = n.Y; }
            if (SelectedPipe is { } p) { PipeDiameter = p.Diameter; PipeWallThickness = p.WallThickness; var center = PipeCenter(p); PositionX = center.X; PositionY = center.Y; }
            SyncNumericParameters();
        }
        finally { _syncEditors = false; }
    }
    private void ReplacePipe(CdsPipe pipe)
    {
        Layout.Pipes[Layout.Pipes.FindIndex(p => p.Id == pipe.Id)] = pipe;
        LayoutSource = L10n.Get("cdsp.layout.edited"); Changed();
    }
    private void SetNodePosition(int index, Point position)
    {
        var node = Layout.Nodes[index];
        var moved = node with { X = Math.Clamp(position.X, 0, node.Kind == "Terminal" ? Layout.Width : Layout.Width - node.Width),
            Y = Math.Clamp(position.Y, 0, node.Kind == "Terminal" ? Layout.Height : Layout.Height - node.Height) };
        ReplaceNode(moved);
    }
    private static bool PortsAligned(Point3D start, Point3D end)
        => Math.Abs(start.X - end.X) < 0.01 || Math.Abs(start.Y - end.Y) < 0.01;

    private static Point[] DefaultBends(Point3D start, Point3D end) => PortsAligned(start, end) ? []
        : [new Point((start.X + end.X) / 2, -start.Y), new Point((start.X + end.X) / 2, -end.Y)];

    private Point SnapNodePosition(CdsNode node, Point position)
    {
        var delta = position - new Point(node.X, node.Y);
        double snapX = 0, snapY = 0, nearestX = 8.01, nearestY = 8.01;
        double? guide = null;
        foreach (var pipe in Layout.Pipes)
        {
            bool from = CdsLayoutComposer.Owner(pipe.From) == node.Id, to = CdsLayoutComposer.Owner(pipe.To) == node.Id;
            if (from == to) continue;
            var own = Layout.Port(from ? pipe.From : pipe.To); var other = Layout.Port(from ? pipe.To : pipe.From);
            double dx = other.X - own.X - delta.X, dy = own.Y - other.Y - delta.Y;
            if (Math.Abs(dx) < nearestX) { nearestX = Math.Abs(dx); snapX = dx; guide = other.X; }
            if (Math.Abs(dy) < nearestY) { nearestY = Math.Abs(dy); snapY = dy; }
        }
        // 已连管的接口优先；未连管的控件也能按中心、左边或右边上下对齐。
        if (!guide.HasValue)
        {
            var candidate = node with { X = position.X, Y = position.Y };
            var own = VerticalAnchors(candidate);
            foreach (var other in Layout.Nodes.Where(n => n.Id != node.Id))
            {
                var targets = VerticalAnchors(other);
                for (int i = 0; i < own.Length; i++)
                {
                    double dx = targets[i] - own[i];
                    if (Math.Abs(dx) >= nearestX) continue;
                    nearestX = Math.Abs(dx); snapX = dx; guide = targets[i];
                }
            }
        }
        VerticalGuideX = guide;
        // 先按鼠标实际位置吸附，未命中时才回到网格，避免网格挡住对齐位置。
        return new Point(guide.HasValue ? position.X + snapX : Math.Round(position.X / 10) * 10,
            nearestY < 8.01 ? position.Y + snapY : Math.Round(position.Y / 10) * 10);
    }
    private static double[] VerticalAnchors(CdsNode node)
    {
        double x = node.Kind == "Terminal" ? node.X - node.Width / 2 : node.X;
        double y = node.Kind == "Terminal" ? node.Y - node.Height / 2 : node.Y;
        var bounds = new System.Windows.Media.RotateTransform(node.Angle, x + node.Width / 2, y + node.Height / 2)
            .TransformBounds(new Rect(x, y, node.Width, node.Height));
        return [(bounds.Left + bounds.Right) / 2, bounds.Left, bounds.Right];
    }
    internal void ClearAlignmentGuide()
    {
        if (!VerticalGuideX.HasValue) return;
        VerticalGuideX = null; SceneChanged?.Invoke();
    }
    private void ReplaceNode(CdsNode moved)
    {
        int index = Layout.Nodes.FindIndex(n => n.Id == moved.Id);
        var node = Layout.Nodes[index];
        if (moved == node) return;
        // 接口对齐后清除旧拐点；高度、横向位置均不同时才需要折线。
        var attached = Layout.Pipes.Where(p => CdsLayoutComposer.Owner(p.From) == node.Id || CdsLayoutComposer.Owner(p.To) == node.Id)
            .Select(p => (Pipe: p, Start: Layout.Port(p.From), End: Layout.Port(p.To))).ToArray();
        Layout.Nodes[index] = moved;
        foreach (var entry in attached)
        {
            var start = Layout.Port(entry.Pipe.From); var end = Layout.Port(entry.Pipe.To);
            if (start == entry.Start && end == entry.End) continue;
            var bends = entry.Pipe.Bends.ToArray();
            if (PortsAligned(start, end)) bends = [];
            else if (bends.Length == 0) bends = DefaultBends(start, end);
            else
            {
                static Point Follow(Point bend, Point3D before, Point3D after)
                    => new(Math.Abs(bend.X - before.X) < 1 ? after.X : bend.X, Math.Abs(bend.Y + before.Y) < 1 ? -after.Y : bend.Y);
                if (CdsLayoutComposer.Owner(entry.Pipe.From) == node.Id) bends[0] = Follow(bends[0], entry.Start, start);
                if (CdsLayoutComposer.Owner(entry.Pipe.To) == node.Id) bends[^1] = Follow(bends[^1], entry.End, end);
            }
            Layout.Pipes[Layout.Pipes.FindIndex(p => p.Id == entry.Pipe.Id)] = entry.Pipe with { Bends = bends };
        }
        SyncEditors(); OnPropertyChanged(nameof(MaxPositionX)); OnPropertyChanged(nameof(MaxPositionY));
        LayoutSource = L10n.Get("cdsp.layout.edited"); Changed();
    }
}
