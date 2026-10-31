using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>XAML 声明的腔体场景根节点。</summary>
public sealed class ChamberDiagramRoot3D : ModelVisual3D
{
    public string ComponentRoot { get; set; } = "";
    public string ChamberType { get; set; } = "";
    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        HardwareVisual3D.NotifySceneConnection(this);
    }
}

public sealed class ChamberDiagramSize
{
    public string Key { get; set; } = "";
    public double Value { get; set; }
}

/// <summary>仅保留工具识别组件所需的元数据；变换与状态绑定直接写在 XAML 中。</summary>
public sealed class ChamberDiagramPart3D : ModelVisual3D
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Component { get; set; } = "";
    public string ParentId { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Yaw { get; set; }
    public double HomeAngle { get; set; } = -90;
    public bool AutoLength { get; set; } = true;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public List<ChamberDiagramSize> Sizes { get; } = [];
    private ModelVisual3D? _visual;
    public ModelVisual3D? Visual
    {
        get => _visual;
        set
        {
            if (_visual is not null) Children.Remove(_visual);
            _visual = value;
            if (value is not null) Children.Insert(0, value);
        }
    }
}
