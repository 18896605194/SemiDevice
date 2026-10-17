using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CdsLayoutPreview;

public partial class PreviewPage
{
    internal void SetTool(string tool)
    {
        if (!ApplyPipeProperties())
        {
            return;
        }
        CancelGesture();
        _tool = tool;
        Stage.Cursor = tool == "draw" ? Cursors.Cross : Cursors.Arrow;
        Refresh(false);
        string status;
        switch (tool)
        {
            case "draw":
                status = "点选蓝色连接口作为起点，再点拐点，最后点终点连接口。Esc / 右键取消。";
                break;
            case "preview":
                status = "运行预览：布局锁定，可在右侧切换模拟状态和液位。";
                break;
            default:
                status = "拖动槽或接口；选中管路后拖动方形拐点，双击线段增加拐点。";
                break;
        }

        Status(status);
    }

    private void OnSelectTool(object sender, RoutedEventArgs args) => SetTool("select");
    private void OnDrawTool(object sender, RoutedEventArgs args) => SetTool("draw");
    private void OnPreviewTool(object sender, RoutedEventArgs args) => SetTool("preview");

    private void OnStageDown(object sender, MouseButtonEventArgs args)
    {
        Focus();
        PointerDown(args.GetPosition(Stage), args.ClickCount);
        if (_dragSnapshot is not null)
        {
            Stage.CaptureMouse();
        }
        args.Handled = true;
    }

    // 鼠标处理和冒烟验证共用同一组坐标入口；GetPosition(Stage) 自动处理 Viewbox 缩放。
    internal void PointerDown(Point point, int clicks = 1)
    {
        if (_tool == "preview")
        {
            return;
        }
        if (!ApplyPipeProperties())
        {
            return;
        }
        _pointer = point;
        if (_tool == "draw")
        {
            DrawClick(point);
            return;
        }
        var pipe = SelectedPipe;
        int handle = pipe?.Bends.FindIndex(bend => (bend - point).Length <= 12) ?? -1;
        if (handle >= 0)
        {
            _bend = handle;
            StartDrag(point);
            Refresh(false);
            return;
        }
        string? labelId = _pipeTextBounds.LastOrDefault(item => item.Value.Contains(point)).Key;
        if (labelId is not null)
        {
            _selection = labelId;
            _bend = -1;
            Refresh(false);
            return;
        }
        var node = Document.Nodes.LastOrDefault(item => NodeBounds(item).Contains(point));
        if (node is not null)
        {
            _selection = node.Id;
            _bend = -1;
            _nodeStart = new Point(node.X, node.Y);
            StartDrag(point);
            Refresh(false);
            return;
        }
        var hit = HitPipe(point);
        _selection = hit.Pipe?.Id;
        _bend = -1;
        if (clicks == 2 && hit.Pipe is not null)
        {
            if (hit.Pipe.Bends.Count >= 100)
            {
                Status("单条管路最多 100 个拐点。");
                return;
            }
            string before = Document.ToXml();
            hit.Pipe.Bends.Insert(hit.Segment, Snap(hit.Projection));
            _bend = hit.Segment;
            _history.Commit(before);
        }
        Refresh(false);
    }

    private void StartDrag(Point point)
    {
        _dragStart = point;
        _dragSnapshot = Document.ToXml();
    }

    private void OnStageMove(object sender, MouseEventArgs args) => PointerMove(args.GetPosition(Stage));

    internal void PointerMove(Point point)
    {
        _pointer = point;
        if (_drawFrom is not null)
        {
            DrawOverlay();
        }
        if (_dragSnapshot is null)
        {
            return;
        }
        if (SelectedNode is { } node)
        {
            Point next = Snap(_nodeStart + (point - _dragStart));
            if (new Point(node.X, node.Y) == next)
            {
                return;
            }
            Document.MoveNode(node, next);
        }
        else if (SelectedPipe is { } pipe && _bend >= 0)
        {
            Point next = Snap(point);
            if (pipe.Bends[_bend] == next)
            {
                return;
            }
            pipe.Bends[_bend] = next;
        }
        UpdateDragPreview();
    }

    private void OnStageUp(object sender, MouseButtonEventArgs args)
    {
        PointerUp();
        Stage.ReleaseMouseCapture();
    }

    internal void PointerUp()
    {
        if (_dragSnapshot is not null)
        {
            _history.Commit(_dragSnapshot);
            _dragSnapshot = null;
            EndDragPreview();
            Refresh(false);
        }
    }

    private void OnStageLostCapture(object sender, MouseEventArgs args) => PointerUp();

    private void DrawClick(Point point)
    {
        string? port = HitPort(point);
        if (_drawFrom is null)
        {
            if (port is null)
            {
                Status("请先点选一个蓝色连接口。");
                return;
            }
            _drawFrom = port;
            _draft.Clear();
            DocumentChanged?.Invoke();
            DrawOverlay();
            return;
        }
        if (port == _drawFrom)
        {
            Status("起点和终点不能是同一个连接口。");
            return;
        }
        var route = new List<Point> { Document.Port(_drawFrom) };
        route.AddRange(_draft);
        AppendOrthogonal(route, port is null ? Snap(point) : Document.Port(port));
        if (route.Count > 102 || Document.Pipes.Count >= 200)
        {
            Status("已达到数量限制：每条管路最多 100 个拐点，最多 200 条管路。");
            return;
        }
        if (port is null)
        {
            _draft.Clear();
            _draft.AddRange(route.Skip(1));
            DrawOverlay();
            return;
        }
        if (Document.Pipes.Any(pipe => (pipe.From == _drawFrom && pipe.To == port) || (pipe.From == port && pipe.To == _drawFrom)))
        {
            Status("这两个连接口之间已有管路。可先修改或删除已有管路，再重新绘制。");
            return;
        }
        string before = Document.ToXml();
        int suffix = 1;
        var ids = Document.Nodes.Select(node => node.Id).Concat(Document.Pipes.Select(pipe => pipe.Id)).ToHashSet();
        while (ids.Contains("Pipe" + suffix))
        {
            suffix++;
        }
        var created = new LayoutPipe
        {
            Id = "Pipe" + suffix, From = _drawFrom, To = port,
            Mode = (string)((ComboBoxItem)NewPipeModePicker.SelectedItem).Tag
        };
        created.Bends.AddRange(route.Skip(1).Take(route.Count - 2));
        Document.Pipes.Add(created);
        _history.Commit(before);
        _selection = created.Id;
        _bend = -1;
        _drawFrom = null;
        _draft.Clear();
        _tool = "select";
        Stage.Cursor = Cursors.Arrow;
        Refresh();
        Status("管路已绘制，可拖动方形拐点调整。右侧可修改此管路的模拟状态。");
    }

    private static void AppendOrthogonal(List<Point> route, Point next)
    {
        Point last = route[^1];
        if ((last - next).Length < 0.1)
        {
            return;
        }
        if (Math.Abs(last.X - next.X) > 0.1 && Math.Abs(last.Y - next.Y) > 0.1)
        {
            route.Add(Math.Abs(next.X - last.X) >= Math.Abs(next.Y - last.Y)
                ? new Point(next.X, last.Y) : new Point(last.X, next.Y));
        }
        route.Add(next);
    }

    private string? HitPort(Point point)
    {
        return Document.PortNames.FirstOrDefault(name => (Document.Port(name) - point).Length <= 15);
    }

    private (LayoutPipe? Pipe, int Segment, Point Projection) HitPipe(Point point)
    {
        LayoutPipe? found = null;
        int segment = -1;
        Point closest = default;
        double distance = 14;
        foreach (var pipe in Document.Pipes)
        {
            var route = Document.Route(pipe);
            for (int i = 0; i < route.Count - 1; i++)
            {
                Vector delta = route[i + 1] - route[i];
                double t = delta.LengthSquared < 0.01 ? 0 : Math.Clamp(Vector.Multiply(point - route[i], delta) / delta.LengthSquared, 0, 1);
                Point projection = route[i] + delta * t;
                double current = (point - projection).Length;
                if (current < distance)
                {
                    found = pipe;
                    segment = i;
                    closest = projection;
                    distance = current;
                }
            }
        }
        return (found, segment, closest);
    }

    private Point Snap(Point point)
    {
        return new Point(Math.Clamp(Math.Round(point.X / 10) * 10, 10, Document.Width - 10),
            Math.Clamp(Math.Round(point.Y / 10) * 10, 10, Document.Height - 10));
    }

    private static Rect NodeBounds(LayoutNode node)
    {
        return node.Kind == "Tank" ? new Rect(node.X, node.Y, node.Width, node.Height)
            : new Rect(node.X - 18, node.Y - 18, 36, 36);
    }

    internal void CancelGesture()
    {
        if (_dragSnapshot is not null)
        {
            _history.Restore(_dragSnapshot);
            _dragSnapshot = null;
            EndDragPreview();
            Stage.ReleaseMouseCapture();
        }
        _drawFrom = null;
        _draft.Clear();
        if (_ready)
        {
            Refresh();
            DocumentChanged?.Invoke();
        }
    }

    private void OnStageRightClick(object sender, MouseButtonEventArgs args)
    {
        CancelGesture();
        Status("已取消当前绘制或拖动。");
        args.Handled = true;
    }

    internal void DeleteSelection()
    {
        if (_tool != "select" || SelectedPipe is not { } pipe)
        {
            return;
        }
        string before = Document.ToXml();
        if (_bend >= 0)
        {
            pipe.Bends.RemoveAt(_bend);
            _bend = -1;
        }
        else
        {
            Document.Pipes.Remove(pipe);
            _selection = null;
        }
        _history.Commit(before);
        Refresh();
    }

    private void OnDelete(object sender, RoutedEventArgs args) => DeleteSelection();

    internal void Undo()
    {
        if (_tool == "preview")
        {
            return;
        }
        CancelGesture();
        _history.Undo();
        _bend = -1;
        Refresh();
    }

    internal void Redo()
    {
        if (_tool == "preview")
        {
            return;
        }
        CancelGesture();
        _history.Redo();
        _bend = -1;
        Refresh();
    }

    private void OnUndo(object sender, RoutedEventArgs args) => Undo();
    private void OnRedo(object sender, RoutedEventArgs args) => Redo();

    private void OnEditorKeyDown(object sender, KeyEventArgs args)
    {
        if (args.OriginalSource is TextBox && args.Key is not (Key.Escape or Key.S or Key.O))
        {
            return;
        }
        if (args.Key == Key.Escape)
        {
            CancelGesture();
            args.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (args.Key)
            {
                case Key.Z: Undo(); args.Handled = true; break;
                case Key.Y: Redo(); args.Handled = true; break;
                case Key.S:
                    if (_embedded)
                    {
                        SaveRequested?.Invoke();
                    }
                    else
                    {
                        SaveInteractive(false);
                    }
                    args.Handled = true;
                    break;
                case Key.O:
                    if (!_embedded)
                    {
                        OpenInteractive();
                        args.Handled = true;
                    }
                    break;
            }
        }
        else if (args.Key == Key.Delete && args.OriginalSource is not TextBox)
        {
            DeleteSelection();
            args.Handled = true;
        }
    }
}
