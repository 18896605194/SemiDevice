using System.Windows;
using System.Windows.Media.Media3D;
using xyz.Client.Presentation.Controls.ThreeD;

namespace ProjectControls;

/// <summary>验证项目自有类型通过公开依赖属性参与导出和绑定，无运行时适配器。</summary>
public sealed class ProjectBowlVisual3D : ModelVisual3D
{
    private readonly BowlVisual3D _body = new();
    public ProjectBowlVisual3D() => Children.Add(_body);
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(nameof(Radius), typeof(double), typeof(ProjectBowlVisual3D),
        new PropertyMetadata(1.59, (d, e) => ((ProjectBowlVisual3D)d)._body.Radius = (double)e.NewValue));
    public bool IsRaised { get => (bool)GetValue(IsRaisedProperty); set => SetValue(IsRaisedProperty, value); }
    public static readonly DependencyProperty IsRaisedProperty = DependencyProperty.Register(nameof(IsRaised), typeof(bool), typeof(ProjectBowlVisual3D),
        new PropertyMetadata(false, (d, e) => ((ProjectBowlVisual3D)d)._body.IsRaised = (bool)e.NewValue));
    public bool IsUnknown { get => (bool)GetValue(IsUnknownProperty); set => SetValue(IsUnknownProperty, value); }
    public static readonly DependencyProperty IsUnknownProperty = DependencyProperty.Register(nameof(IsUnknown), typeof(bool), typeof(ProjectBowlVisual3D),
        new PropertyMetadata(false, (d, e) => ((ProjectBowlVisual3D)d)._body.IsUnknown = (bool)e.NewValue));
}
