using System.Windows;
using System.Windows.Media;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.Controls;

/// <summary>
/// 机械手一帧的显示姿态，数值均为动画中的显示值。
/// </summary>
internal sealed record RobotSceneFrame(
    RobotArmType ArmType,
    RobotDisplayStatus Status,
    double Rotation,
    double Travel,
    IReadOnlyList<RobotSceneArm> Arms,
    double TimeMs);

internal sealed record RobotSceneArm(int Arm, double Extension, double Heading, bool HasWafer);

/// <summary>
/// 机械手绘制共用的几何换算与画刷。设计尺寸 400×400，机械手中心在控件中心（再加平移），按控件大小等比缩放；
/// 本体层、手臂层与手臂上的片控件都用这里的换算，保证三者对齐。
/// </summary>
internal static class RobotDrawing
{
    #region 几何常量（设计尺寸下的像素）

    public const double DesignSize = 400;
    public const double BaseRadius = 58;
    public const double StatusRingRadius = 67;
    public const double WaferRadius = 42;
    public const double RetractedReach = 24;
    public const double ExtendedReach = 150;
    public const double EffectorLength = 58;
    public const double LinkLength = 66;
    public const double ShoulderOffset = 20;
    public const double WristOffset = 12;
    public const double ExtendedThreshold = 0.01;

    #endregion

    #region 坐标换算

    /// <summary>
    /// 本体坐标（转台中心为原点）→ 控件坐标：先在设计尺寸下水平平移，再缩放，最后移到控件中心；平移量跟机械手一起缩放。
    /// </summary>
    public static Matrix BodyMatrix(Size size, RobotSceneFrame frame)
    {
        double scale = Scale(size);
        var matrix = Matrix.Identity;
        matrix.Translate(frame.Travel, 0);
        matrix.Scale(scale, scale);
        matrix.Translate(size.Width / 2, size.Height / 2);
        return matrix;
    }

    /// <summary>
    /// 手臂局部坐标（转台中心为原点、手臂正前方朝上）→ 控件坐标：叠放偏移 → 旋转 → 缩放 → 平移。
    /// </summary>
    public static Matrix ArmMatrix(Size size, RobotSceneFrame frame, int index)
    {
        double stack = StackOffset(frame, index);
        var matrix = Matrix.Identity;
        matrix.Translate(stack, stack);
        matrix.Rotate(ArmAngle(frame, index));
        matrix.Append(BodyMatrix(size, frame));
        return matrix;
    }

    /// <summary>
    /// 手臂上片心在控件坐标中的位置。
    /// </summary>
    public static Point WaferCenter(Size size, RobotSceneFrame frame, int index)
    {
        return ArmMatrix(size, frame, index).Transform(new Point(0, -Reach(frame.Arms[index].Extension)));
    }

    public static double Scale(Size size)
    {
        return Math.Min(size.Width, size.Height) / DesignSize;
    }

    public static double ArmAngle(RobotSceneFrame frame, int index)
    {
        return frame.Rotation + frame.Arms[index].Heading;
    }

    /// <summary>
    /// 片心到转台中心的距离。
    /// </summary>
    public static double Reach(double extension)
    {
        return RetractedReach + Math.Clamp(extension, 0, 1) * (ExtendedReach - RetractedReach);
    }

    /// <summary>
    /// 收回时多只手臂叠在一起：按手臂号错开一点，下层露边；伸出过程中回到中线。
    /// </summary>
    public static double StackOffset(RobotSceneFrame frame, int index)
    {
        return (frame.Arms.Count - 1 - index) * 3.5 * (1 - Math.Min(frame.Arms[index].Extension * 4, 1));
    }

    /// <summary>
    /// 叠放次序（从下到上）：伸出的手臂在最上层；收回的手臂里带片的压在空手之上，片不被叉挡住。
    /// </summary>
    public static IReadOnlyList<int> DrawOrder(RobotSceneFrame frame)
    {
        return Enumerable.Range(0, frame.Arms.Count)
            .OrderBy(index => frame.Arms[index].Extension > ExtendedThreshold ? 1 : 0)
            .ThenBy(index => frame.Arms[index].Extension)
            .ThenBy(index => frame.Arms[index].HasWafer ? 1 : 0)
            .ThenBy(index => frame.Arms[index].Arm)
            .ToList();
    }

    /// <summary>
    /// 蛙式连杆互相交叠：有手臂伸出时，其余手臂压暗，突出正在干活的那只。
    /// </summary>
    public static bool IsDimmed(RobotSceneFrame frame, int index)
    {
        return frame.ArmType == RobotArmType.FrogLeg
               && frame.Arms[index].Extension <= ExtendedThreshold
               && frame.Arms.Any(arm => arm.Extension > ExtendedThreshold);
    }

    /// <summary>
    /// 朝向角（0 = 正上方，顺时针）对应的单位向量。
    /// </summary>
    public static Vector Direction(double degrees)
    {
        double radians = degrees * Math.PI / 180;
        return new Vector(Math.Sin(radians), -Math.Cos(radians));
    }

    /// <summary>
    /// 屏幕方向的偏移换算到旋转后的局部坐标，让阴影始终落在同一侧。
    /// </summary>
    public static Vector ScreenToLocal(Vector offset, double degrees)
    {
        double radians = -degrees * Math.PI / 180;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        return new Vector(offset.X * cos - offset.Y * sin, offset.X * sin + offset.Y * cos);
    }

    public static Rect Offset(Rect rect, Vector offset)
    {
        rect.Offset(offset);
        return rect;
    }

    #endregion

    #region 画刷与画笔

    public static readonly Brush ShadowBrush = Frozen(Solid(0x55, 0, 0, 0));
    public static readonly Brush BoltBrush = Frozen(Solid(0xFF, 0x1B, 0x1E, 0x23));

    public static readonly Brush MetalBrush = Frozen(new LinearGradientBrush(Color.FromRgb(0x72, 0x7C, 0x8B), Color.FromRgb(0x3B, 0x42, 0x4D), 90));
    public static readonly Pen MetalPen = Frozen(new Pen(Solid(0xFF, 0x1C, 0x20, 0x26), 1.1));

    #endregion

    #region 工具

    public static Color WithAlpha(Color color, byte alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    public static SolidColorBrush Solid(byte alpha, byte red, byte green, byte blue)
    {
        return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
    }

    public static Pen RoundPen(Brush brush, double thickness)
    {
        return Frozen(new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
    }

    public static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    #endregion
}

/// <summary>
/// 主题色取自资源字典（DarkColors.xaml），取不到用默认值。
/// </summary>
internal sealed class RobotPalette
{
    public RobotPalette(FrameworkElement owner)
    {
        Accent = Resolve(owner, "DarkAccent", Color.FromRgb(0x42, 0xA5, 0xF5));
        Running = Resolve(owner, "DarkRunningStatus", Color.FromRgb(0x43, 0xA0, 0x47));
        Warning = Resolve(owner, "DarkWarningStatus", Color.FromRgb(0xFB, 0xC0, 0x2D));
        Alarm = Resolve(owner, "DarkAlarmStatus", Color.FromRgb(0xEF, 0x53, 0x50));
        Disabled = Resolve(owner, "DarkDisabledText", Color.FromRgb(0x9E, 0x9E, 0x9E));
    }

    public Color Accent { get; }

    public Color Running { get; }

    public Color Warning { get; }

    public Color Alarm { get; }

    public Color Disabled { get; }

    public Color StatusColor(RobotDisplayStatus status)
    {
        return status switch
        {
            RobotDisplayStatus.Idle => Running,
            RobotDisplayStatus.Busy => Accent,
            RobotDisplayStatus.NotReady => Warning,
            RobotDisplayStatus.Alarm => Alarm,
            _ => Disabled,
        };
    }

    private static Color Resolve(FrameworkElement owner, string key, Color fallback)
    {
        return owner.TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;
    }
}
