using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CdsLayoutPreview;

public partial class PreviewPage
{
    private readonly Dictionary<string, Polyline> _dragLines = [];

    private void UpdateDragPreview()
    {
        if (_dragLines.Count == 0)
        {
            var affected = Document.Pipes.Where(pipe => SelectedNode is { } node
                ? pipe.From.StartsWith(node.Id + ".", StringComparison.Ordinal) || pipe.To.StartsWith(node.Id + ".", StringComparison.Ordinal)
                : pipe.Id == _selection);
            foreach (var pipe in affected)
            {
                // 只在这一手势开始时隐藏相关三维管路；松开后复用原实例更新路径。
                Viewport.Children.Remove(_pipes[pipe.Id]);
                var line = new Polyline
                {
                    Stroke = new SolidColorBrush(pipe.Appearance.FlowColor), StrokeThickness = Math.Max(2, pipe.Appearance.Diameter / 3),
                    StrokeLineJoin = PenLineJoin.Round, Opacity = 0.85
                };
                _dragLines.Add(pipe.Id, line);
                DragLayer.Children.Add(line);
            }
        }
        if (SelectedNode is { Kind: "Tank" } tank)
        {
            _bodies[tank.Id].Transform = tank.TankTransform();
        }
        foreach (var pipe in Document.Pipes)
        {
            if (_dragLines.TryGetValue(pipe.Id, out var line))
            {
                line.Points = new PointCollection(Document.Route(pipe));
            }
        }
        // 拖动只更新轻量手柄和线框，不更新属性面板、不生成 XML、不重建三维网格。
        DrawOverlay();
    }

    private void EndDragPreview()
    {
        DragLayer.Children.Clear();
        _dragLines.Clear();
    }
}
