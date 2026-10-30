using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Windows.Input;

namespace CdsAssemblyPreview;

public partial class CdsScene
{
    // 缩放属于视图状态，不写入布局坐标或装机配置。
    public static readonly DependencyProperty ZoomPercentProperty = DependencyProperty.Register(nameof(ZoomPercent), typeof(double), typeof(CdsScene),
        new FrameworkPropertyMetadata(100d, (d, _) => ((CdsScene)d).UpdateScale(),
            (_, value) => double.IsFinite((double)value) ? Math.Clamp((double)value, 10, 400) : 100d));
    public double ZoomPercent { get => (double)GetValue(ZoomPercentProperty); set => SetValue(ZoomPercentProperty, value); }
    public IRelayCommand ZoomInCommand { get; private set; } = null!;
    public IRelayCommand ZoomOutCommand { get; private set; } = null!;
    public IRelayCommand ActualSizeCommand { get; private set; } = null!;
    public IRelayCommand FitCommand { get; private set; } = null!;
    private bool _autoFit = true, _updatingFit;
    private Point? _zoomAnchor;
    internal bool IsAutoFit => _autoFit;

    private void InitializeZoomCommands()
    {
        ZoomInCommand = new RelayCommand(DoZoomIn, () => ZoomPercent < 400);
        ZoomOutCommand = new RelayCommand(DoZoomOut, () => ZoomPercent > 10);
        ActualSizeCommand = new RelayCommand(DoActualSize);
        FitCommand = new RelayCommand(DoFit);
    }
    private void DoZoomIn() => SetZoom(ZoomPercent * 1.2);
    private void DoZoomOut() => SetZoom(ZoomPercent / 1.2);
    private void DoActualSize() => SetZoom(100);
    private void DoFit() { _autoFit = true; UpdateFit(); }
    private void SetZoom(double percent)
    {
        _autoFit = false;
        SetCurrentValue(ZoomPercentProperty, Math.Clamp(Math.Round(percent, 1), 10, 400));
    }
    private void UpdateScale()
    {
        if (SceneScale is null || Scroller is null) return;
        var anchor = _zoomAnchor ?? new Point(Scroller.ViewportWidth / 2, Scroller.ViewportHeight / 2);
        var canvasPoint = Scroller.TranslatePoint(anchor, Stage);
        SceneScale.ScaleX = SceneScale.ScaleY = ZoomPercent / 100;
        if (!_updatingFit)
        {
            _autoFit = false; Scroller.UpdateLayout();
            var moved = Stage.TranslatePoint(canvasPoint, Scroller);
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset + moved.X - anchor.X);
            Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + moved.Y - anchor.Y);
        }
        ZoomInCommand.NotifyCanExecuteChanged(); ZoomOutCommand.NotifyCanExecuteChanged();
    }
    private void UpdateFit()
    {
        if (!_autoFit || Scroller.ActualWidth <= 4 || Scroller.ActualHeight <= 4 || !(Stage.Width > 0) || !(Stage.Height > 0)) return;
        _updatingFit = true;
        try
        {
            SetCurrentValue(ZoomPercentProperty, Math.Clamp(Math.Floor(1000 * Math.Min((Scroller.ActualWidth - 4) / Stage.Width,
                (Scroller.ActualHeight - 4) / Stage.Height)) / 10, 10, 400));
            Scroller.ScrollToHorizontalOffset(0); Scroller.ScrollToVerticalOffset(0);
        }
        finally { _updatingFit = false; }
    }
    internal void ZoomAt(int delta, Point anchor)
    {
        if (delta == 0 || Stage.IsMouseCaptured) return;
        _zoomAnchor = anchor;
        try { SetZoom(ZoomPercent * Math.Pow(1.2, delta / 120d)); }
        finally { _zoomAnchor = null; }
    }
    private void OnZoomWheel(object sender, MouseWheelEventArgs e)
    {
        ZoomAt(e.Delta, e.GetPosition(Scroller)); e.Handled = true;
    }
    private void OnSceneSizeChanged(object sender, SizeChangedEventArgs e) => UpdateFit();
}
