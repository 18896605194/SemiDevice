using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 供液槽三维外观：透明圆筒 + 里面的液体，原点在罐底中心、Y 向上
/// </summary>
public sealed class TankVisual3D : HardwareVisual3D
{
    #region 尺寸（罐半径 1 的示意单位）

    /// <summary>
    /// 罐高
    /// </summary>
    public const double TankHeight = 2.65;

    /// <summary>
    /// 上下沿外径（罐的整个宽度按它算）
    /// </summary>
    public const double RimRadius = 1.012;

    private const double WallRadius = 1;
    private const double RimInnerRadius = 0.965;
    private const double RimHalfThickness = 0.014;
    private const double FloorRadius = 0.985;
    private const double FloorHeight = 0.025;
    private const double LiquidRadius = 0.935;
    private const double LiquidBottom = 0.035;

    /// <summary>
    /// 液满时液面离罐顶留的空
    /// </summary>
    private const double LiquidHeadroom = 0.07;

    private const int Segments = 160;

    #endregion

    #region 材质

    private const double FloorBrightness = 0.68;

    /// <summary>
    /// 后半圈罐壁比前半圈淡
    /// </summary>
    private const double RearWallShare = 0.35;

    private const double FrontWallShare = 0.65;
    private const double WallShine = 85;
    private const double SurfaceShine = 28;
    private const double ShineShare = 0.42;

    private static readonly Color FallbackWall = Color.FromRgb(174, 190, 201);
    private static readonly Color FallbackLiquid = Color.FromRgb(36, 142, 191);
    private static readonly Color FallbackLiquidSurface = Color.FromRgb(96, 188, 222);
    private static readonly Color FallbackLiquidAlarm = Color.FromRgb(193, 89, 85);
    private static readonly Color FallbackLiquidAlarmSurface = Color.FromRgb(230, 155, 140);
    private static readonly Color FallbackShine = Color.FromRgb(192, 192, 192);

    #endregion

    private readonly GeometryModel3D _rearWall = new();
    private readonly GeometryModel3D _frontWall = new();
    private readonly GeometryModel3D _liquidSide;
    private readonly GeometryModel3D _liquidSurface;
    private readonly Model3DGroup _liquid = new();
    private readonly Model3DGroup _liquidSlot = new();
    private readonly ScaleTransform3D _liquidScale = new(1, 1, 1);

    public TankVisual3D()
    {
        // 先画后半圈罐壁、罐底、液体，最后画前半圈：透明的罐壁要在液体后面先画、前面后画
        Model.Children.Add(_rearWall);
        AddMesh(Disc(FloorRadius, FloorHeight), FloorBrightness);
        _liquidSide = new GeometryModel3D(Band(LiquidRadius, 0, LiquidRadius, 1, 0, Math.Tau), null);
        _liquidSurface = new GeometryModel3D(Disc(LiquidRadius, 1), null);
        _liquid.Children.Add(_liquidSide);
        _liquid.Children.Add(_liquidSurface);
        var transform = new Transform3DGroup();
        transform.Children.Add(_liquidScale);
        transform.Children.Add(new TranslateTransform3D(0, LiquidBottom, 0));
        _liquid.Transform = transform;
        Model.Children.Add(_liquidSlot);
        Model.Children.Add(_frontWall);

        // 上下沿：薄薄一圈（外面、顶面、里面），没有底座、支脚、管路、盖子
        foreach (double y in new[] { 0d, TankHeight })
        {
            AddMesh(Band(RimRadius, y - RimHalfThickness, RimRadius, y + RimHalfThickness, 0, Math.Tau));
            AddMesh(Band(RimRadius, y + RimHalfThickness, RimInnerRadius, y + RimHalfThickness, 0, Math.Tau));
            AddMesh(Band(RimInnerRadius, y + RimHalfThickness, RimInnerRadius, y - RimHalfThickness, 0, Math.Tau));
        }

        UpdateWallGeometry();
        UpdateWallMaterial();
        UpdateLiquidMaterial();
        UpdateLiquidLevel();
    }

    #region 依赖属性

    /// <summary>
    /// 液面高度占罐高的比例（0～1），0 不画液体
    /// </summary>
    public double FillFraction
    {
        get => (double)GetValue(FillFractionProperty);
        set => SetValue(FillFractionProperty, value);
    }

    public static readonly DependencyProperty FillFractionProperty = DependencyProperty.Register(
        nameof(FillFraction), typeof(double), typeof(TankVisual3D),
        new PropertyMetadata(0d, (sender, _) => ((TankVisual3D)sender).UpdateLiquidLevel()), IsFraction);

    /// <summary>
    /// 液体画成报警红
    /// </summary>
    public bool IsAlarm
    {
        get => (bool)GetValue(IsAlarmProperty);
        set => SetValue(IsAlarmProperty, value);
    }

    public static readonly DependencyProperty IsAlarmProperty = DependencyProperty.Register(
        nameof(IsAlarm), typeof(bool), typeof(TankVisual3D),
        new PropertyMetadata(false, (sender, _) => ((TankVisual3D)sender).UpdateLiquidMaterial()));

    /// <summary>
    /// 罐壁透明度（0～1）
    /// </summary>
    public double WallTransparency
    {
        get => (double)GetValue(WallTransparencyProperty);
        set => SetValue(WallTransparencyProperty, value);
    }

    public static readonly DependencyProperty WallTransparencyProperty = DependencyProperty.Register(
        nameof(WallTransparency), typeof(double), typeof(TankVisual3D),
        new PropertyMetadata(0.72, (sender, _) => ((TankVisual3D)sender).UpdateWallMaterial()), IsFraction);

    /// <summary>
    /// 相机的方位角（度），用来分前后两半罐壁
    /// </summary>
    public double Azimuth
    {
        get => (double)GetValue(AzimuthProperty);
        set => SetValue(AzimuthProperty, value);
    }

    public static readonly DependencyProperty AzimuthProperty = DependencyProperty.Register(
        nameof(Azimuth), typeof(double), typeof(TankVisual3D),
        new PropertyMetadata(0d, (sender, _) => ((TankVisual3D)sender).UpdateWallGeometry()), IsFinite);

    private static bool IsFraction(object value)
    {
        return value is double fraction && double.IsFinite(fraction) && fraction >= 0 && fraction <= 1;
    }

    private static bool IsFinite(object value)
    {
        return value is double number && double.IsFinite(number);
    }

    #endregion

    #region 更新

    /// <summary>
    /// 朝相机的半圈是前壁
    /// </summary>
    private void UpdateWallGeometry()
    {
        double azimuth = Azimuth * Math.PI / 180;
        double top = TankHeight - FloorHeight;
        _rearWall.Geometry = Band(WallRadius, FloorHeight, WallRadius, top, azimuth + Math.PI / 2, azimuth + Math.PI * 1.5);
        _frontWall.Geometry = Band(WallRadius, FloorHeight, WallRadius, top, azimuth - Math.PI / 2, azimuth + Math.PI / 2);
    }

    private void UpdateWallMaterial()
    {
        Color wall = HardwareTheme.ColorOf("DarkHardwareTankWallColor", FallbackWall);
        double opacity = 1 - WallTransparency;
        _rearWall.Material = Glass(wall, opacity * RearWallShare, WallShine);
        _rearWall.BackMaterial = _rearWall.Material;
        _frontWall.Material = Glass(wall, opacity * FrontWallShare, WallShine);
        _frontWall.BackMaterial = _frontWall.Material;
    }

    private void UpdateLiquidMaterial()
    {
        Color side = IsAlarm
            ? HardwareTheme.ColorOf("DarkHardwareLiquidAlarmColor", FallbackLiquidAlarm)
            : HardwareTheme.ColorOf("DarkHardwareLiquidColor", FallbackLiquid);
        Color surface = IsAlarm
            ? HardwareTheme.ColorOf("DarkHardwareLiquidAlarmSurfaceColor", FallbackLiquidAlarmSurface)
            : HardwareTheme.ColorOf("DarkHardwareLiquidSurfaceColor", FallbackLiquidSurface);
        _liquidSide.Material = Glass(side, 1, 0);
        _liquidSide.BackMaterial = _liquidSide.Material;
        _liquidSurface.Material = Glass(surface, 1, SurfaceShine);
        _liquidSurface.BackMaterial = _liquidSurface.Material;
    }

    private void UpdateLiquidLevel()
    {
        double fraction = FillFraction;
        if (fraction <= 0)
        {
            _liquidSlot.Children.Clear();
            return;
        }

        _liquidScale.ScaleY = fraction * (TankHeight - LiquidBottom - LiquidHeadroom);
        if (_liquidSlot.Children.Count == 0)
        {
            _liquidSlot.Children.Add(_liquid);
        }
    }

    /// <summary>
    /// 带透明度的漫反射，shine 大于 0 再叠一层主光色的高光
    /// </summary>
    private static Material Glass(Color color, double opacity, double shine)
    {
        byte alpha = (byte)Math.Round(255 * Math.Clamp(opacity, 0, 1));
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B))));
        if (shine > 0)
        {
            Color light = HardwareTheme.ColorOf("DarkHardwareKeyLightColor", FallbackShine);
            var highlight = Color.FromArgb((byte)Math.Round(alpha * ShineShare), light.R, light.G, light.B);
            group.Children.Add(new SpecularMaterial(new SolidColorBrush(highlight), shine));
        }

        group.Freeze();
        return group;
    }

    #endregion

    #region 网格

    /// <summary>
    /// 绕 Y 轴从 start 转到 end（弧度）的一段侧面，0 度在 +Z
    /// </summary>
    private static MeshGeometry3D Band(double radius0, double y0, double radius1, double y1, double start, double end)
    {
        var mesh = new MeshGeometry3D();
        int count = Math.Max(Segments / 6, (int)Math.Ceiling(Math.Abs(end - start) / Math.Tau * Segments));
        for (int i = 0; i <= count; i++)
        {
            double angle = start + (end - start) * i / count;
            var normal = new Vector3D((y1 - y0) * Math.Sin(angle), radius0 - radius1, (y1 - y0) * Math.Cos(angle));
            normal.Normalize();
            mesh.Positions.Add(new Point3D(radius0 * Math.Sin(angle), y0, radius0 * Math.Cos(angle)));
            mesh.Positions.Add(new Point3D(radius1 * Math.Sin(angle), y1, radius1 * Math.Cos(angle)));
            mesh.Normals.Add(normal);
            mesh.Normals.Add(normal);
            if (i == count)
            {
                continue;
            }

            int k = i * 2;
            foreach (int index in new[] { k, k + 2, k + 3, k, k + 3, k + 1 })
            {
                mesh.TriangleIndices.Add(index);
            }
        }

        mesh.Freeze();
        return mesh;
    }

    /// <summary>
    /// 高度 y 处朝上的圆片
    /// </summary>
    private static MeshGeometry3D Disc(double radius, double y)
    {
        var mesh = new MeshGeometry3D();
        var up = new Vector3D(0, 1, 0);
        mesh.Positions.Add(new Point3D(0, y, 0));
        mesh.Normals.Add(up);
        for (int i = 0; i <= Segments; i++)
        {
            double angle = i * Math.Tau / Segments;
            mesh.Positions.Add(new Point3D(radius * Math.Sin(angle), y, radius * Math.Cos(angle)));
            mesh.Normals.Add(up);
            if (i < Segments)
            {
                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(i + 1);
                mesh.TriangleIndices.Add(i + 2);
            }
        }

        mesh.Freeze();
        return mesh;
    }

    #endregion
}
