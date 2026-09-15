using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using xyz.Client.Presentation.Models;
using static xyz.Client.Presentation.Controls.RobotDrawing;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 单只手臂的绘制层：直伸直出为伸缩导轨 + 滑座 + 叉，蛙式为左右连杆 + 腕部 + 叉。
/// 手臂上的片不在这里画，由 Wafer 控件放在叉上。
/// </summary>
internal sealed class RobotArmLayer : FrameworkElement
{
    #region 画刷与画笔

    private static readonly Brush RailOuterBrush = Frozen(new LinearGradientBrush(Color.FromRgb(0x6C, 0x76, 0x85), Color.FromRgb(0x3A, 0x41, 0x4C), 0));
    private static readonly Brush RailInnerBrush = Frozen(new LinearGradientBrush(Color.FromRgb(0xA0, 0xAA, 0xB8), Color.FromRgb(0x56, 0x5F, 0x6C), 0));
    private static readonly Pen RailPen = Frozen(new Pen(Solid(0xFF, 0x1E, 0x22, 0x28), 1));

    private static readonly Brush ForkBrush = Frozen(new LinearGradientBrush(new GradientStopCollection
    {
        new(Color.FromRgb(0xE6, 0xEB, 0xF1), 0.0),
        new(Color.FromRgb(0xB3, 0xBC, 0xC8), 0.5),
        new(Color.FromRgb(0x84, 0x8F, 0x9D), 1.0),
    }, new Point(0, 0), new Point(1, 0)));

    private static readonly Pen ForkPen = Frozen(new Pen(Solid(0xFF, 0x4E, 0x57, 0x63), 0.9));
    private static readonly Brush PadBrush = Frozen(Solid(0xFF, 0x1D, 0x23, 0x29));
    private static readonly Pen PadIdlePen = Frozen(new Pen(Solid(0xFF, 0x59, 0x63, 0x6F), 1));
    private static readonly Vector[] PadOffsets = [new(-20, -30), new(20, -30), new(0, 23)];
    private static readonly Brush WaferShadowBrush = Frozen(Solid(0x60, 0, 0, 0));
    private static readonly Brush ArmNumberBrush = Frozen(Solid(0xFF, 0xDD, 0xE3, 0xEA));

    private static readonly Pen UpperLinkShadow = RoundPen(Solid(0x55, 0, 0, 0), 16);
    private static readonly Pen UpperLinkOutline = RoundPen(Solid(0xFF, 0x13, 0x16, 0x1B), 18);
    private static readonly Pen UpperLinkBody = RoundPen(Solid(0xFF, 0x5E, 0x68, 0x78), 15);
    private static readonly Pen UpperLinkShine = RoundPen(Solid(0x2E, 0xFF, 0xFF, 0xFF), 4);
    private static readonly Pen ForeLinkShadow = RoundPen(Solid(0x50, 0, 0, 0), 13);
    private static readonly Pen ForeLinkOutline = RoundPen(Solid(0xFF, 0x13, 0x16, 0x1B), 14.5);
    private static readonly Pen ForeLinkBody = RoundPen(Solid(0xFF, 0x72, 0x7C, 0x8C), 12);
    private static readonly Pen ForeLinkShine = RoundPen(Solid(0x33, 0xFF, 0xFF, 0xFF), 3.2);
    private static readonly Brush JointBrush = Frozen(new RadialGradientBrush(Color.FromRgb(0x9E, 0xA8, 0xB6), Color.FromRgb(0x3A, 0x40, 0x4A)) { GradientOrigin = new Point(0.35, 0.3) });
    private static readonly Pen JointPen = Frozen(new Pen(Solid(0xFF, 0x16, 0x19, 0x1E), 1.1));

    #endregion

    private RobotSceneFrame? _frame;
    private int _index;
    private RobotPalette? _palette;
    private Typeface? _typeface;

    public RobotArmLayer()
    {
        IsHitTestVisible = false;
    }

    public void Update(RobotSceneFrame frame, int index)
    {
        _frame = frame;
        _index = index;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_frame is null || _index >= _frame.Arms.Count || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var frame = _frame;
        var arm = frame.Arms[_index];
        double angle = ArmAngle(frame, _index);
        var palette = _palette ??= new RobotPalette(this);
        _typeface ??= new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Vector shadow = ScreenToLocal(new Vector(3, 5), angle);
        var center = new Point(0, 0);

        dc.PushTransform(new MatrixTransform(ArmMatrix(RenderSize, frame, _index)));
        if (frame.ArmType == RobotArmType.FrogLeg)
        {
            DrawFrogLegArm(dc, center, arm, angle, shadow, palette, pixelsPerDip);
        }
        else
        {
            DrawLinearArm(dc, center, arm, angle, shadow, palette, pixelsPerDip);
        }

        dc.Pop();
    }

    /// <summary>
    /// 直伸直出：两节伸缩导轨 + 滑座 + 叉。局部坐标以转台中心为原点、正前方朝上。
    /// </summary>
    private void DrawLinearArm(DrawingContext dc, Point center, RobotSceneArm arm, double angle, Vector shadow, RobotPalette palette, double pixelsPerDip)
    {
        var waferCenter = new Point(center.X, center.Y - Reach(arm.Extension));
        double carriageY = waferCenter.Y + 50;
        double front = center.Y - 44;

        // 伸缩导轨：伸出后露出转台前沿的部分，两节套筒
        double railTop = carriageY + 10;
        if (railTop < front)
        {
            double middle = (railTop + front) / 2;
            dc.DrawRectangle(ShadowBrush, null, new Rect(center.X - 9 + shadow.X, railTop + shadow.Y, 18, front - railTop));
            dc.DrawRoundedRectangle(RailOuterBrush, RailPen, new Rect(center.X - 9, middle, 18, front - middle + 4), 3, 3);
            dc.DrawRoundedRectangle(RailInnerBrush, RailPen, new Rect(center.X - 6, railTop, 12, middle - railTop + 3), 3, 3);
        }

        var carriage = new Rect(center.X - 17, carriageY - 12, 34, 24);
        dc.DrawRoundedRectangle(ShadowBrush, null, Offset(carriage, shadow), 6, 6);
        dc.DrawRoundedRectangle(MetalBrush, MetalPen, carriage, 6, 6);
        DrawArmNumber(dc, new Point(center.X, carriageY), arm, angle, palette, pixelsPerDip);
        DrawEffector(dc, waferCenter, carriage.Top + 2, arm, shadow, palette);
    }

    /// <summary>
    /// 蛙式：左右肩关节 → 大臂 → 肘关节 → 小臂 → 腕部，腕部沿中线伸缩，肘关节由两段连杆长度解算。
    /// </summary>
    private void DrawFrogLegArm(DrawingContext dc, Point center, RobotSceneArm arm, double angle, Vector shadow, RobotPalette palette, double pixelsPerDip)
    {
        var waferCenter = new Point(center.X, center.Y - Reach(arm.Extension));
        var wrist = new Point(center.X, waferCenter.Y + EffectorLength);
        var shoulderLeft = new Point(center.X - ShoulderOffset, center.Y + 10);
        var shoulderRight = new Point(center.X + ShoulderOffset, center.Y + 10);
        var pivotLeft = new Point(wrist.X - WristOffset, wrist.Y);
        var pivotRight = new Point(wrist.X + WristOffset, wrist.Y);
        Point elbowLeft = Elbow(shoulderLeft, pivotLeft, outwardRight: false);
        Point elbowRight = Elbow(shoulderRight, pivotRight, outwardRight: true);

        dc.DrawLine(UpperLinkShadow, shoulderLeft + shadow, elbowLeft + shadow);
        dc.DrawLine(UpperLinkShadow, shoulderRight + shadow, elbowRight + shadow);
        dc.DrawLine(ForeLinkShadow, elbowLeft + shadow, pivotLeft + shadow);
        dc.DrawLine(ForeLinkShadow, elbowRight + shadow, pivotRight + shadow);

        DrawLink(dc, shoulderLeft, elbowLeft, UpperLinkOutline, UpperLinkBody, UpperLinkShine);
        DrawLink(dc, shoulderRight, elbowRight, UpperLinkOutline, UpperLinkBody, UpperLinkShine);
        DrawLink(dc, elbowLeft, pivotLeft, ForeLinkOutline, ForeLinkBody, ForeLinkShine);
        DrawLink(dc, elbowRight, pivotRight, ForeLinkOutline, ForeLinkBody, ForeLinkShine);

        DrawJoint(dc, shoulderLeft, 10);
        DrawJoint(dc, shoulderRight, 10);
        DrawJoint(dc, elbowLeft, 7.5);
        DrawJoint(dc, elbowRight, 7.5);

        var hub = new Rect(wrist.X - 24, wrist.Y - 11, 48, 22);
        dc.DrawRoundedRectangle(ShadowBrush, null, Offset(hub, shadow), 9, 9);
        dc.DrawRoundedRectangle(MetalBrush, MetalPen, hub, 9, 9);
        DrawJoint(dc, pivotLeft, 5.5);
        DrawJoint(dc, pivotRight, 5.5);
        DrawArmNumber(dc, wrist, arm, angle, palette, pixelsPerDip);
        DrawEffector(dc, waferCenter, hub.Top + 2, arm, shadow, palette);
    }

    /// <summary>
    /// 叉（末端执行器）+ 真空吸盘；手臂上有片时再画片的投影，片本身由 Wafer 控件盖在上面。
    /// </summary>
    private static void DrawEffector(DrawingContext dc, Point waferCenter, double stemBottom, RobotSceneArm arm, Vector shadow, RobotPalette palette)
    {
        var fork = ForkGeometry(waferCenter, stemBottom);
        dc.PushTransform(new TranslateTransform(shadow.X, shadow.Y));
        dc.DrawGeometry(ShadowBrush, null, fork);
        dc.Pop();
        dc.DrawGeometry(ForkBrush, ForkPen, fork);

        Pen padPen = arm.HasWafer ? new Pen(new SolidColorBrush(WithAlpha(palette.Accent, 0xC0)), 1.1) : PadIdlePen;
        foreach (var offset in PadOffsets)
        {
            dc.DrawEllipse(PadBrush, padPen, waferCenter + offset, 3, 3);
        }

        if (arm.HasWafer)
        {
            dc.DrawEllipse(WaferShadowBrush, null, waferCenter + shadow, WaferRadius, WaferRadius);
        }
    }

    private void DrawArmNumber(DrawingContext dc, Point position, RobotSceneArm arm, double angle, RobotPalette palette, double pixelsPerDip)
    {
        Brush brush = arm.Extension > ExtendedThreshold ? new SolidColorBrush(palette.Accent) : ArmNumberBrush;
        var text = new FormattedText(
            arm.Arm.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            _typeface!, 11.5, brush, pixelsPerDip);
        dc.PushTransform(new RotateTransform(-angle, position.X, position.Y));
        dc.DrawText(text, new Point(position.X - text.Width / 2, position.Y - text.Height / 2));
        dc.Pop();
    }

    private static void DrawLink(DrawingContext dc, Point from, Point to, Pen outline, Pen body, Pen shine)
    {
        dc.DrawLine(outline, from, to);
        dc.DrawLine(body, from, to);
        var offset = new Vector(-1.5, -1.5);
        Vector span = to - from;
        dc.DrawLine(shine, from + span * 0.12 + offset, from + span * 0.88 + offset);
    }

    private static void DrawJoint(DrawingContext dc, Point position, double radius)
    {
        dc.DrawEllipse(JointBrush, JointPen, position, radius, radius);
        dc.DrawEllipse(BoltBrush, null, position, radius * 0.32, radius * 0.32);
    }

    /// <summary>
    /// U 形叉：两齿尖伸出片边，底梁接回滑座（或腕部）。
    /// </summary>
    private static Geometry ForkGeometry(Point waferCenter, double stemBottom)
    {
        double x = waferCenter.X;
        double y = waferCenter.Y;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(x - 25, y - 46), true, true);
            context.ArcTo(new Point(x - 15, y - 46), new Size(5, 5), 0, false, SweepDirection.Clockwise, true, true);
            context.LineTo(new Point(x - 15, y + 16), true, true);
            context.LineTo(new Point(x + 15, y + 16), true, true);
            context.LineTo(new Point(x + 15, y - 46), true, true);
            context.ArcTo(new Point(x + 25, y - 46), new Size(5, 5), 0, false, SweepDirection.Clockwise, true, true);
            context.LineTo(new Point(x + 25, y + 22), true, true);
            context.ArcTo(new Point(x + 17, y + 30), new Size(8, 8), 0, false, SweepDirection.Clockwise, true, true);
            context.LineTo(new Point(x + 7, y + 30), true, true);
            context.LineTo(new Point(x + 7, stemBottom), true, true);
            context.LineTo(new Point(x - 7, stemBottom), true, true);
            context.LineTo(new Point(x - 7, y + 30), true, true);
            context.LineTo(new Point(x - 17, y + 30), true, true);
            context.ArcTo(new Point(x - 25, y + 22), new Size(8, 8), 0, false, SweepDirection.Clockwise, true, true);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// 两连杆解算肘关节位置（大臂、小臂等长）；outwardRight 为 true 取靠右的解，否则取靠左的解。
    /// </summary>
    private static Point Elbow(Point shoulder, Point wrist, bool outwardRight)
    {
        Vector span = wrist - shoulder;
        double length = Math.Max(span.Length, 0.001);
        double distance = Math.Min(length, 2 * LinkLength - 0.5);
        Vector unit = span / length;
        double along = distance / 2;
        double height = Math.Sqrt(Math.Max(0, LinkLength * LinkLength - along * along));
        Point foot = shoulder + unit * along;
        var normal = new Vector(-unit.Y, unit.X);
        Point first = foot + normal * height;
        Point second = foot - normal * height;
        return outwardRight == (first.X > second.X) ? first : second;
    }
}
