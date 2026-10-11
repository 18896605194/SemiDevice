using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace xyz.Client.Presentation.Controls.ThreeD;

/// <summary>
/// 三维件取主题色：三维对象不在逻辑树里、用不了 DynamicResource，从应用资源里按 key 取一次。
/// </summary>
internal static class HardwareTheme
{
    /// <summary>
    /// Color 直接用、SolidColorBrush 取它的颜色；渐变刷（片的"工艺中"这类）取各段平均——三维面上不铺渐变，只要一个认得出的颜色。
    /// 取不到（没并主题）用 fallback。
    /// </summary>
    public static Color ColorOf(string key, Color fallback)
    {
        object? resource = Application.Current?.TryFindResource(key);
        if (resource is Color color)
        {
            return color;
        }

        if (resource is SolidColorBrush solid)
        {
            return solid.Color;
        }

        if (resource is GradientBrush gradient && gradient.GradientStops.Count > 0)
        {
            double r = 0;
            double g = 0;
            double b = 0;
            foreach (var stop in gradient.GradientStops)
            {
                r += stop.Color.R;
                g += stop.Color.G;
                b += stop.Color.B;
            }

            int count = gradient.GradientStops.Count;
            return Color.FromRgb((byte)Math.Round(r / count), (byte)Math.Round(g / count), (byte)Math.Round(b / count));
        }

        return fallback;
    }

    /// <summary>漫反射材质，颜色按亮度系数压暗 / 提亮（同一种金属灰分出深浅几档）。</summary>
    public static Material Diffuse(Color color, double brightness)
    {
        byte Channel(byte value) => (byte)Math.Clamp(Math.Round(value * brightness), 0, 255);
        var material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(color.A, Channel(color.R), Channel(color.G), Channel(color.B))));
        material.Freeze();
        return material;
    }
}
