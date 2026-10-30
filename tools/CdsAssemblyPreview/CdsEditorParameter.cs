using CommunityToolkit.Mvvm.ComponentModel;
using xyz.Client.Presentation.Localization;

namespace CdsAssemblyPreview;

public abstract class CdsEditorParameter(string key) : ObservableObject
{
    public string Key { get; } = key;
    public string Title => L10n.Find("cdsp.parameter." + Key) ?? Key;
}

public sealed class CdsNumberParameter(string key, double initial, double minimum, double maximum, Action<double> apply, string unitKey = "cdsp.units.canvas") : CdsEditorParameter(key)
{
    public double Minimum { get; } = minimum;
    private double _maximum = maximum;
    public double Maximum { get => _maximum; set => SetProperty(ref _maximum, value); }
    public string Unit => L10n.Get(unitKey);
    private bool _updating;
    private double _value = initial;
    public double Value
    {
        get => _value;
        set { if (double.IsFinite(value) && value >= Minimum && value <= Maximum && SetProperty(ref _value, value) && !_updating) apply(value); }
    }
    public void Sync(double value) { _updating = true; try { SetProperty(ref _value, value, nameof(Value)); } finally { _updating = false; } }
}

public sealed class CdsBooleanParameter(string key, bool initial, Action<bool> apply) : CdsEditorParameter(key)
{
    private bool _value = initial;
    public bool Value { get => _value; set { if (SetProperty(ref _value, value)) apply(value); } }
}

public sealed class CdsTextParameter(string key, string initial, Func<string, string?> apply) : CdsEditorParameter(key)
{
    private string _value = initial;
    public string Value
    {
        get => _value;
        set { if (SetProperty(ref _value, value)) Error = apply(value) ?? ""; }
    }
    private string _error = "";
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
}
