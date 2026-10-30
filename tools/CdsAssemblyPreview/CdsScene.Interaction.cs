using System.Windows;
using System.Windows.Input;

namespace CdsAssemblyPreview;

public partial class CdsScene
{
    private Point _pressedAt;
    private bool _dragging;
    private string? _dragPipeId;
    private int _dragBend = -1;

    // 与鼠标入口使用同一套场景坐标命中逻辑，拖动不依赖会被刷新替换的图形子节点。
    internal bool PointerDown(Point point, int clickCount = 1)
    {
        if (_model is null) return false;
        _model.ClearAlignmentGuide();
        _dragId = null; _dragPipeId = null; _dragBend = -1;
        if (_model.ConnectMode)
        {
            string? port = _model.Layout.Ports.FirstOrDefault(name =>
            { var p = _model.Layout.Port(name); return (new Point(p.X, -p.Y) - point).Length <= 9; });
            if (port is not null) { _model.Connect(port); return true; }
        }
        if (!_model.IsSimulationMode && _model.SelectedPipe is { } selected)
        {
            int bend = selected.Bends.Select((p, i) => (Point: p, Index: i)).Where(p => (p.Point - point).Length <= 10)
                .Select(p => p.Index).DefaultIfEmpty(-1).First();
            if (bend >= 0) { Stage.CaptureMouse(); _dragPipeId = selected.Id; _dragBend = bend; _pressedAt = point; _dragging = false; return true; }
        }
        var node = _model.Layout.Nodes.OrderBy(n => n.Kind == "Valve" ? 0 : n.Kind == "Terminal" ? 1 : 2).FirstOrDefault(n => Bounds(n).Contains(point));
        if (node is not null)
        {
            _model.Select(node.Id);
            if (clickCount == 2 && node.Kind == "Valve")
            {
                var command = _model.ValveStates.GetValueOrDefault(_model.SelectionPath) == true ? _model.CloseValveCommand : _model.OpenValveCommand;
                if (command.CanExecute(null)) command.Execute(null);
                return true;
            }
            _dragOffset = point - new Point(node.X, node.Y); _pressedAt = point; _dragging = false;
            Stage.CaptureMouse(); _dragId = node.Id;
            return true;
        }
        var pipe = _model.Layout.Pipes.Select(p => (Pipe: p, Distance: PipeDistance(p, point))).OrderBy(p => p.Distance)
            .FirstOrDefault(p => p.Distance <= Math.Max(12, p.Pipe.Diameter / 2 + 5)).Pipe;
        if (pipe is null) return false;
        _model.SelectPipe(pipe.Id); _pressedAt = point; _dragging = false; _dragOffset = point - _model.PipeCenter(pipe);
        Stage.CaptureMouse(); _dragPipeId = pipe.Id; return true;
    }
    internal void PointerMove(Point point)
    {
        if (_dragId is null && _dragPipeId is null) return;
        if (!_dragging && (point - _pressedAt).Length < 4) return;
        _dragging = true;
        if (_dragId is not null) _model?.Move(_dragId, point - _dragOffset);
        if (_dragPipeId is not null && _dragBend >= 0) _model?.MovePipeBend(_dragPipeId, _dragBend, point);
        else if (_dragPipeId is not null) _model?.MovePipe(_dragPipeId, point - _dragOffset);
    }
    internal void PointerUp() { _dragId = null; _dragPipeId = null; _dragBend = -1; _dragging = false; if (Stage.IsMouseCaptured) Stage.ReleaseMouseCapture(); _model?.ClearAlignmentGuide(); }
    internal IInputElement? HitAt(Point point) => Stage.InputHitTest(point);

    private static Rect Bounds(CdsNode n) => new System.Windows.Media.RotateTransform(n.Angle,
        n.Kind == "Terminal" ? n.X : n.X + n.Width / 2, n.Kind == "Terminal" ? n.Y : n.Y + n.Height / 2).TransformBounds(n.Kind switch
    {
        "Valve" => new Rect(n.X - 16, n.Y - 31, n.Width + 32, n.Height + 60),
        "Terminal" => new Rect(n.X - n.Width / 2 - 6, n.Y - n.Height / 2 - 6, n.Width + 12, n.Height + 12),
        _ => new Rect(n.X, n.Y, n.Width, n.Height)
    });
    private double PipeDistance(CdsPipe pipe, Point point)
    {
        var from = _model!.Layout.Port(pipe.From); var to = _model.Layout.Port(pipe.To);
        var points = new[] { new Point(from.X, -from.Y) }.Concat(pipe.Bends).Append(new Point(to.X, -to.Y)).ToArray();
        return points.Zip(points.Skip(1), (a, b) =>
        {
            Vector segment = b - a; double t = segment.LengthSquared < 0.001 ? 0 : Math.Clamp(Vector.Multiply(point - a, segment) / segment.LengthSquared, 0, 1);
            return (point - (a + t * segment)).Length;
        }).Min();
    }
    internal void FocusEditor() => Keyboard.Focus(Stage);
    private void OnDown(object sender, MouseButtonEventArgs e) { if (PointerDown(e.GetPosition(Stage), e.ClickCount)) { FocusEditor(); e.Handled = true; } }
    private void OnMove(object sender, MouseEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) PointerMove(e.GetPosition(Stage)); }
    private void OnUp(object sender, MouseButtonEventArgs e) => PointerUp();
    private void OnLostCapture(object sender, MouseEventArgs e) { if (!Stage.IsMouseCaptured) { _dragId = null; _dragPipeId = null; _dragBend = -1; _dragging = false; _model?.ClearAlignmentGuide(); } }
}
