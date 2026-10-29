using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Localization;

namespace ChamberLayoutEditor;

/// <summary>预先声明的外形尺寸；布局只能保存这些尺寸键，不解释任意属性或代码。</summary>
public sealed class ChamberDimension(string key, double initial, double minimum, double maximum) : ObservableObject
{
    private readonly double _initial = initial;
    public string Key { get; } = key;
    public double Minimum { get; } = minimum;
    public double Maximum { get; } = maximum;
    public string Title => Key.StartsWith("Nozzle", StringComparison.Ordinal)
        ? L10n.Get("chambereditor.nozzle_dimension", Key.Split('.')[0], L10n.Get("chambereditor.dimension." + Key.Split('.')[1]))
        : L10n.Get("chambereditor.dimension." + Key);
    private double _value = initial;
    public double Value { get => _value; set => SetProperty(ref _value, value); }
    private bool _isEditable = true;
    public bool IsEditable { get => _isEditable; set => SetProperty(ref _isEditable, value); }
    public double SafeValue => double.IsFinite(Value) ? Math.Clamp(Value, Minimum, Maximum) : _initial;
    public bool IsValid => double.IsFinite(Value) && Value >= Minimum && Value <= Maximum;

    internal static ObservableCollection<ChamberDimension> Create(string kind, bool hasLift, int nozzles)
    {
        var result = new ObservableCollection<ChamberDimension>();
        void Add(string key, double initial, double min = 0.01, double max = 10) => result.Add(new(key, initial, min, max));
        switch (kind)
        {
            case "Disk": Add("Radius", 1.32); Add("Thickness", 0.045, 0.005, 2); Add("SpindleHeight", 0.32); break;
            case "Bowl": Add("Radius", 1.59); Add("HeightScale", 1, 0.1, 4); break;
            case "Door": Add("Width", 2.2); Add("Height", 0.58); Add("DepthScale", 1, 0.1, 4); break;
            case "ArmAssembly":
                Add("ArmLength", 2.31625, 0.05, 15);
                Add("ArmWidth", 0.161); Add("ArmThickness", 0.0595, 0.005, 2); Add("PivotRadius", 0.1995);
                Add("CupRadius", 0.224); Add("CupHeight", 0.4585);
                if (hasLift) { Add("LiftWidthScale", 1, 0.1, 4); Add("LiftHeightScale", 1, 0.1, 4); }
                for (int i = 0; i < Math.Clamp(nozzles, 0, 8); i++)
                { Add($"Nozzle{i + 1}.LengthScale", 1, 0.1, 4); Add($"Nozzle{i + 1}.DiameterScale", 1, 0.1, 4); }
                break;
        }
        return result;
    }
}
