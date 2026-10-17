using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Xml.Linq;

namespace CdsLayoutPreview;

/// <summary>长度为画布显示单位，不代表设备管径的毫米数。</summary>
public sealed record PipeAppearance
{
    public double Diameter { get; init; } = 16;
    public double WallThickness { get; init; } = 1.6;
    public double BendRadius { get; init; } = 18;
    public double WallOpacity { get; init; } = 0.18;
    public double FlowSpeed { get; init; } = 65;
    public Color WallColor { get; init; } = Color.FromRgb(180, 180, 180);
    public Color FlowColor { get; init; } = Color.FromRgb(96, 188, 222);
    public bool ReverseFlow { get; init; }
    public bool IsFilled { get; init; } = true;
    public bool IsAnimationEnabled { get; init; } = true;

    public void Validate()
    {
        if (!double.IsFinite(Diameter) || Diameter < 2 || Diameter > 80)
        {
            throw new InvalidDataException("管径应在 2～80 个画布单位之间。");
        }
        if (!double.IsFinite(WallThickness) || WallThickness <= 0 || WallThickness >= Diameter / 2)
        {
            throw new InvalidDataException("管壁厚度必须大于 0，且小于管径的一半。");
        }
        if (!double.IsFinite(BendRadius) || BendRadius < 0 || BendRadius > 300)
        {
            throw new InvalidDataException("转弯半径应在 0～300 之间。");
        }
        if (!double.IsFinite(WallOpacity) || WallOpacity < 0 || WallOpacity > 1)
        {
            throw new InvalidDataException("管壁不透明度应在 0～1 之间。");
        }
        if (!double.IsFinite(FlowSpeed) || FlowSpeed < 0 || FlowSpeed > 500)
        {
            throw new InvalidDataException("动画速度应在 0～500 之间。");
        }
    }

    public static PipeAppearance Read(XElement element)
    {
        var result = new PipeAppearance
        {
            Diameter = Number(element, "Diameter", 16), WallThickness = Number(element, "WallThickness", 1.6),
            BendRadius = Number(element, "BendRadius", 18), WallOpacity = Number(element, "WallOpacity", 0.18),
            FlowSpeed = Number(element, "FlowSpeed", 65),
            WallColor = ParseColor((string?)element.Attribute("WallColor") ?? "#B4B4B4"),
            FlowColor = ParseColor((string?)element.Attribute("FlowColor") ?? "#60BCDE"),
            ReverseFlow = Boolean(element, "ReverseFlow", false), IsFilled = Boolean(element, "IsFilled", true),
            IsAnimationEnabled = Boolean(element, "IsAnimationEnabled", true)
        };
        result.Validate();
        return result;
    }

    public void Write(XElement element)
    {
        element.Add(new XAttribute("Diameter", Diameter), new XAttribute("WallThickness", WallThickness),
            new XAttribute("BendRadius", BendRadius), new XAttribute("WallOpacity", WallOpacity), new XAttribute("FlowSpeed", FlowSpeed),
            new XAttribute("WallColor", WallColor), new XAttribute("FlowColor", FlowColor),
            new XAttribute("ReverseFlow", ReverseFlow), new XAttribute("IsFilled", IsFilled), new XAttribute("IsAnimationEnabled", IsAnimationEnabled));
    }

    public static Color ParseColor(string text)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(text.Trim());
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException("颜色请填写 #RRGGBB、#AARRGGBB 或有效的颜色名。", exception);
        }
    }

    private static double Number(XElement element, string name, double fallback)
    {
        string? text = (string?)element.Attribute(name);
        if (text is null)
        {
            return fallback;
        }
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            throw new InvalidDataException($"管路属性 {name} 不是有效数值。");
        }
        return value;
    }

    private static bool Boolean(XElement element, string name, bool fallback)
    {
        string? text = (string?)element.Attribute(name);
        if (text is null)
        {
            return fallback;
        }
        if (!bool.TryParse(text, out bool value))
        {
            throw new InvalidDataException($"管路属性 {name} 应为 true 或 false。");
        }
        return value;
    }
}
