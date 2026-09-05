using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace xyz.Client.Views;

/// <summary>
/// 可缩放的单个 Load Port Job 操作面板。
/// </summary>
public partial class LoadPortPanel : UserControl
{
    private static readonly string[] DefaultSequences = ["Default Sequence"];
    private static readonly string[] DefaultDirections = ["TOP TO BOTTOM", "BOTTOM TO TOP"];

    public LoadPortPanel()
    {
        InitializeComponent();

        SetCurrentValue(SequenceOptionsProperty, DefaultSequences);
        SetCurrentValue(DirectionOptionsProperty, DefaultDirections);
        SetCurrentValue(SelectedSequenceProperty, DefaultSequences[0]);
        SetCurrentValue(SelectedDirectionProperty, DefaultDirections[0]);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(LoadPortPanel),
            new PropertyMetadata("LOAD PORT JOB CONTROL"));

    public string PortName
    {
        get => (string)GetValue(PortNameProperty);
        set => SetValue(PortNameProperty, value);
    }

    public static readonly DependencyProperty PortNameProperty =
        DependencyProperty.Register(nameof(PortName), typeof(string), typeof(LoadPortPanel),
            new PropertyMetadata("LOAD PORT 01"));

    public string Slot
    {
        get => (string)GetValue(SlotProperty);
        set => SetValue(SlotProperty, value);
    }

    public static readonly DependencyProperty SlotProperty =
        DependencyProperty.Register(nameof(Slot), typeof(string), typeof(LoadPortPanel),
            new FrameworkPropertyMetadata("01", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public IEnumerable? SequenceOptions
    {
        get => (IEnumerable?)GetValue(SequenceOptionsProperty);
        set => SetValue(SequenceOptionsProperty, value);
    }

    public static readonly DependencyProperty SequenceOptionsProperty =
        DependencyProperty.Register(nameof(SequenceOptions), typeof(IEnumerable), typeof(LoadPortPanel),
            new PropertyMetadata(null));

    public object? SelectedSequence
    {
        get => GetValue(SelectedSequenceProperty);
        set => SetValue(SelectedSequenceProperty, value);
    }

    public static readonly DependencyProperty SelectedSequenceProperty =
        DependencyProperty.Register(nameof(SelectedSequence), typeof(object), typeof(LoadPortPanel),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string CjStatus
    {
        get => (string)GetValue(CjStatusProperty);
        set => SetValue(CjStatusProperty, value);
    }

    public static readonly DependencyProperty CjStatusProperty =
        DependencyProperty.Register(nameof(CjStatus), typeof(string), typeof(LoadPortPanel),
            new PropertyMetadata("READY"));

    public bool IsCjReady
    {
        get => (bool)GetValue(IsCjReadyProperty);
        set => SetValue(IsCjReadyProperty, value);
    }

    public static readonly DependencyProperty IsCjReadyProperty =
        DependencyProperty.Register(nameof(IsCjReady), typeof(bool), typeof(LoadPortPanel),
            new PropertyMetadata(true));

    public IEnumerable? DirectionOptions
    {
        get => (IEnumerable?)GetValue(DirectionOptionsProperty);
        set => SetValue(DirectionOptionsProperty, value);
    }

    public static readonly DependencyProperty DirectionOptionsProperty =
        DependencyProperty.Register(nameof(DirectionOptions), typeof(IEnumerable), typeof(LoadPortPanel),
            new PropertyMetadata(null));

    public object? SelectedDirection
    {
        get => GetValue(SelectedDirectionProperty);
        set => SetValue(SelectedDirectionProperty, value);
    }

    public static readonly DependencyProperty SelectedDirectionProperty =
        DependencyProperty.Register(nameof(SelectedDirection), typeof(object), typeof(LoadPortPanel),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public bool IsOnline
    {
        get => (bool)GetValue(IsOnlineProperty);
        set => SetValue(IsOnlineProperty, value);
    }

    public static readonly DependencyProperty IsOnlineProperty =
        DependencyProperty.Register(nameof(IsOnline), typeof(bool), typeof(LoadPortPanel),
            new PropertyMetadata(true));

    public bool IsCarrierPresent
    {
        get => (bool)GetValue(IsCarrierPresentProperty);
        set => SetValue(IsCarrierPresentProperty, value);
    }

    public static readonly DependencyProperty IsCarrierPresentProperty =
        DependencyProperty.Register(nameof(IsCarrierPresent), typeof(bool), typeof(LoadPortPanel),
            new PropertyMetadata(true));

    public bool IsClamped
    {
        get => (bool)GetValue(IsClampedProperty);
        set => SetValue(IsClampedProperty, value);
    }

    public static readonly DependencyProperty IsClampedProperty =
        DependencyProperty.Register(nameof(IsClamped), typeof(bool), typeof(LoadPortPanel),
            new PropertyMetadata(true));

    public ICommand? CreateJobCommand
    {
        get => (ICommand?)GetValue(CreateJobCommandProperty);
        set => SetValue(CreateJobCommandProperty, value);
    }

    public static readonly DependencyProperty CreateJobCommandProperty =
        DependencyProperty.Register(nameof(CreateJobCommand), typeof(ICommand), typeof(LoadPortPanel),
            new PropertyMetadata(null));

    public ICommand? StartJobCommand
    {
        get => (ICommand?)GetValue(StartJobCommandProperty);
        set => SetValue(StartJobCommandProperty, value);
    }

    public static readonly DependencyProperty StartJobCommandProperty =
        DependencyProperty.Register(nameof(StartJobCommand), typeof(ICommand), typeof(LoadPortPanel),
            new PropertyMetadata(null));

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(LoadPortPanel),
            new PropertyMetadata(null));

    public IEnumerable? LoadPorts
    {
        get => (IEnumerable?)GetValue(LoadPortsProperty);
        set => SetValue(LoadPortsProperty, value);
    }

    public static readonly DependencyProperty LoadPortsProperty =
        DependencyProperty.Register(nameof(LoadPorts), typeof(IEnumerable), typeof(LoadPortPanel),
            new PropertyMetadata(null));
}
