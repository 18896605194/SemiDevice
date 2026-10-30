using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ChamberLayoutEditor;

public sealed partial class ChamberLayoutScene
{
    // 视图缩放不改变装机配置、组件尺寸或布局坐标。
    public static readonly DependencyProperty ZoomPercentProperty = DependencyProperty.Register(nameof(ZoomPercent), typeof(double), typeof(ChamberLayoutScene),
        new FrameworkPropertyMetadata(100d, (d, _) => ((ChamberLayoutScene)d).UpdateZoom(),
            (_, value) => double.IsFinite((double)value) ? Math.Clamp((double)value, 10, 400) : 100d));
    public double ZoomPercent { get => (double)GetValue(ZoomPercentProperty); set => SetValue(ZoomPercentProperty, value); }
    public IRelayCommand ZoomInCommand { get; private set; } = null!;
    public IRelayCommand ZoomOutCommand { get; private set; } = null!;
    public IRelayCommand ActualSizeCommand { get; private set; } = null!;
    public IRelayCommand FitCommand { get; private set; } = null!;
    private double ReferenceWidth => Math.Max(7.8, Math.Max(_model?.Document?.BaseLength ?? 0, _model?.Document?.BaseWidth ?? 0) * 1.3);
    internal double CameraWidth => _camera.Width;
    internal Rect3D ContentBounds => VisualTreeHelper.GetDescendantBounds(_world);

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
    private void SetZoom(double value) => SetCurrentValue(ZoomPercentProperty, Math.Clamp(Math.Round(value, 1), 10, 400));
    private void UpdateZoom()
    {
        FinishGesture(); UpdateCamera();
        ZoomInCommand.NotifyCanExecuteChanged(); ZoomOutCommand.NotifyCanExecuteChanged();
    }
    internal void ZoomView(int delta)
    {
        if (delta != 0) SetZoom(ZoomPercent * Math.Pow(1.2, delta / 120d));
    }
    private void DoFit()
    {
        FinishGesture(); Apply();
        var bounds = VisualTreeHelper.GetDescendantBounds(_world);
        if (bounds.IsEmpty || ActualWidth <= 0 || ActualHeight <= 0) { SetZoom(100); return; }
        var (_, right, up) = Basis();
        var target = _viewPreset == ChamberViewPreset.Top ? new Point3D() : new Point3D(0, 0.3, 0);
        double halfWidth = 0, halfHeight = 0;
        foreach (double x in new[] { bounds.X, bounds.X + bounds.SizeX })
            foreach (double y in new[] { bounds.Y, bounds.Y + bounds.SizeY })
                foreach (double z in new[] { bounds.Z, bounds.Z + bounds.SizeZ })
                {
                    var delta = new Point3D(x, y, z) - target;
                    halfWidth = Math.Max(halfWidth, Math.Abs(Vector3D.DotProduct(delta, right)));
                    halfHeight = Math.Max(halfHeight, Math.Abs(Vector3D.DotProduct(delta, up)));
                }
        double aspect = ActualWidth / ActualHeight;
        double required = Math.Max(halfWidth, halfHeight * aspect) * 2.4;
        SetZoom(required > 0 ? 100 * ReferenceWidth * Math.Max(1, aspect / 1.35) / required : 100);
    }
}
