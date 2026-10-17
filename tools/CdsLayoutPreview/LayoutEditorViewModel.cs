using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using xyz.Client.Presentation.Models;

namespace CdsLayoutPreview;

/// <summary>固定的编辑器 ViewModel；每份布局保存为 XML，不生成项目源码。</summary>
public sealed class LayoutEditorViewModel
{
    public TankModel Tank { get; } = new();
    public LayoutHistory History { get; }
    public PipePropertiesViewModel PipeProperties { get; }

    public LayoutEditorViewModel(LayoutDocument document, Action applyPipeProperties)
    {
        History = new LayoutHistory(document);
        PipeProperties = new PipePropertiesViewModel(applyPipeProperties);
    }
}

public sealed class PipePropertiesViewModel : ObservableObject
{
    private string _diameter = "16";
    private string _wallThickness = "1.6";
    private string _bendRadius = "18";
    private string _wallOpacity = "0.18";
    private string _flowSpeed = "65";
    private string _wallColor = "#B4B4B4";
    private string _flowColor = "#60BCDE";
    private string _medium = "IPA";
    private string _text = "";
    private string _error = "";
    private bool _reverseFlow;
    private bool _isFilled = true;
    private bool _isAnimationEnabled = true;
    private string? _loadedId;
    private PipeAppearance? _loadedAppearance;
    private string? _loadedMedium;
    private string? _loadedText;

    public string Diameter { get => _diameter; set => SetProperty(ref _diameter, value); }
    public string WallThickness { get => _wallThickness; set => SetProperty(ref _wallThickness, value); }
    public string BendRadius { get => _bendRadius; set => SetProperty(ref _bendRadius, value); }
    public string WallOpacity { get => _wallOpacity; set => SetProperty(ref _wallOpacity, value); }
    public string FlowSpeed { get => _flowSpeed; set => SetProperty(ref _flowSpeed, value); }
    public string WallColor { get => _wallColor; set => SetProperty(ref _wallColor, value); }
    public string FlowColor { get => _flowColor; set => SetProperty(ref _flowColor, value); }
    public string Medium { get => _medium; set => SetProperty(ref _medium, value); }
    public string Text { get => _text; set => SetProperty(ref _text, value); }
    public bool ReverseFlow { get => _reverseFlow; set => SetProperty(ref _reverseFlow, value); }
    public bool IsFilled { get => _isFilled; set => SetProperty(ref _isFilled, value); }
    public bool IsAnimationEnabled { get => _isAnimationEnabled; set => SetProperty(ref _isAnimationEnabled, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public IRelayCommand ApplyCommand { get; }

    public PipePropertiesViewModel(Action apply)
    {
        ApplyCommand = new RelayCommand(apply);
    }

    public void Load(LayoutPipe? pipe, bool force = false)
    {
        if (pipe is null)
        {
            _loadedId = null;
            _loadedAppearance = null;
            Error = "";
            return;
        }
        if (!force && _loadedId == pipe.Id && _loadedAppearance == pipe.Appearance && _loadedMedium == pipe.Medium && _loadedText == pipe.Text)
        {
            return;
        }
        _loadedId = pipe.Id;
        _loadedAppearance = pipe.Appearance;
        _loadedMedium = pipe.Medium;
        _loadedText = pipe.Text;
        Text = pipe.Text;
        Diameter = Format(pipe.Appearance.Diameter);
        WallThickness = Format(pipe.Appearance.WallThickness);
        BendRadius = Format(pipe.Appearance.BendRadius);
        WallOpacity = Format(pipe.Appearance.WallOpacity);
        FlowSpeed = Format(pipe.Appearance.FlowSpeed);
        WallColor = pipe.Appearance.WallColor.ToString();
        FlowColor = pipe.Appearance.FlowColor.ToString();
        Medium = pipe.Medium;
        ReverseFlow = pipe.Appearance.ReverseFlow;
        IsFilled = pipe.Appearance.IsFilled;
        IsAnimationEnabled = pipe.Appearance.IsAnimationEnabled;
        Error = "";
    }

    public bool TryRead(out PipeAppearance appearance, out string medium)
    {
        appearance = new PipeAppearance();
        medium = Medium.Trim();
        try
        {
            if (Text.Length > 120 || Text.Any(char.IsControl))
            {
                throw new InvalidDataException("管路文字最多 120 个字符，不能包含控制字符。");
            }
            if (medium.Length == 0 || medium.Length > 80)
            {
                throw new InvalidDataException("介质名称需要 1～80 个字符。");
            }
            appearance = new PipeAppearance
            {
                Diameter = Number(Diameter, "管径"), WallThickness = Number(WallThickness, "管壁厚度"),
                BendRadius = Number(BendRadius, "转弯半径"), WallOpacity = Number(WallOpacity, "管壁不透明度"),
                FlowSpeed = Number(FlowSpeed, "动画速度"), WallColor = PipeAppearance.ParseColor(WallColor), FlowColor = PipeAppearance.ParseColor(FlowColor),
                ReverseFlow = ReverseFlow, IsFilled = IsFilled, IsAnimationEnabled = IsAnimationEnabled
            };
            appearance.Validate();
            Error = "";
            return true;
        }
        catch (InvalidDataException exception)
        {
            Error = exception.Message;
            return false;
        }
    }

    public bool HasPendingChanges => _loadedId is not null &&
        (Diameter != Format(_loadedAppearance!.Diameter) || WallThickness != Format(_loadedAppearance.WallThickness)
        || BendRadius != Format(_loadedAppearance.BendRadius) || WallOpacity != Format(_loadedAppearance.WallOpacity)
        || FlowSpeed != Format(_loadedAppearance.FlowSpeed) || WallColor != _loadedAppearance.WallColor.ToString()
        || FlowColor != _loadedAppearance.FlowColor.ToString() || Medium != _loadedMedium || Text != _loadedText
        || ReverseFlow != _loadedAppearance.ReverseFlow || IsFilled != _loadedAppearance.IsFilled || IsAnimationEnabled != _loadedAppearance.IsAnimationEnabled);

    private static string Format(double value) => value.ToString("G", CultureInfo.InvariantCulture);

    private static double Number(string text, string label)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
        {
            throw new InvalidDataException($"{label}请填写有效数字。");
        }
        return value;
    }
}
