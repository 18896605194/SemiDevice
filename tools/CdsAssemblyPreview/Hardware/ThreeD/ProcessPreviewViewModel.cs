using CommunityToolkit.Mvvm.Input;
using xyz.Client.DataModels.ViewModels;

namespace CdsAssemblyPreview;

public sealed class ProcessPreviewViewModel : BaseViewModel
{
    #region Column
    public HardwareDemo Pump { get; private set; } = null!;
    public HardwareDemo Heater { get; private set; } = null!;
    public HardwareDemo Leak { get; private set; } = null!;
    private bool _showInternals = true;
    public bool ShowInternals { get => _showInternals; set => SetProperty(ref _showInternals, value); }
    private double _powerPercent = 65;
    public double PowerPercent { get => _powerPercent; set { if (double.IsFinite(value) && value is >= 0 and <= 100 && SetProperty(ref _powerPercent, value)) OnPropertyChanged(nameof(PowerFraction)); } }
    public double PowerFraction => PowerPercent / 100;
    #endregion
    #region Command
    public IRelayCommand<HardwareState> PumpStateCommand { get; }
    public IRelayCommand<HardwareState> HeaterStateCommand { get; }
    public IRelayCommand<HardwareState> LeakStateCommand { get; }
    #endregion
    #region Service
    private readonly HardwareCatalogViewModel _catalog;
    private bool _initialized;
    #endregion
    public ProcessPreviewViewModel(HardwareCatalogViewModel catalog)
    {
        _catalog = catalog;
        PumpStateCommand = new RelayCommand<HardwareState>(DoPumpState);
        HeaterStateCommand = new RelayCommand<HardwareState>(DoHeaterState);
        LeakStateCommand = new RelayCommand<HardwareState>(DoLeakState);
    }
    public override void Init()
    {
        if (_initialized) return;
        Pump = _catalog.Items.Single(i => i.Definition.Kind == FluidHardwareKind.CentrifugalPump);
        Heater = _catalog.Items.Single(i => i.Definition.Kind == FluidHardwareKind.InlineHeater);
        Leak = _catalog.Items.Single(i => i.Definition.Kind == FluidHardwareKind.LeakSensor);
        _initialized = true;
    }
    private void DoPumpState(HardwareState state) => ApplyState(Pump, state);
    private void DoHeaterState(HardwareState state) => ApplyState(Heater, state);
    private void DoLeakState(HardwareState state) => Leak.State = state;
    private static void ApplyState(HardwareDemo item, HardwareState state)
    {
        if (state == HardwareState.Unknown) item.Reading = null;
        else if (item.Reading is null) item.Reading = item.Definition.DefaultValue;
        item.State = state;
    }
}
