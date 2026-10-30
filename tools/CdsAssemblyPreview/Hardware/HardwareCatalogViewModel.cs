using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using xyz.Client.DataModels.ViewModels;

namespace CdsAssemblyPreview;

public sealed class HardwareCatalogViewModel : BaseViewModel
{
    #region Column
    public ObservableCollection<HardwareDemo> Items { get; } = [];
    public ObservableCollection<HardwareGroup> Groups { get; } = [];
    public ICollectionView ItemsView { get; private set; } = null!;
    private HardwareDemo? _selected;
    public HardwareDemo? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            if (_selected is not null) _selected.IsSelected = false;
            SetProperty(ref _selected, value);
            if (value is not null) value.IsSelected = true;
        }
    }
    private HardwareGroup? _group;
    public HardwareGroup? Group { get => _group; set { if (SetProperty(ref _group, value)) RefreshFilter(); } }
    private string _query = "";
    public string Query { get => _query; set { if (SetProperty(ref _query, value)) RefreshFilter(); } }
    public int Count => Items.Count;
    #endregion
    #region Command
    public IRelayCommand<HardwareDemo> SelectCommand { get; }
    public IRelayCommand<HardwareState> StateCommand { get; }
    public IRelayCommand ResetCommand { get; }
    #endregion
    #region Service
    private bool _initialized;
    #endregion
    public HardwareCatalogViewModel()
    {
        SelectCommand = new RelayCommand<HardwareDemo>(DoSelect);
        StateCommand = new RelayCommand<HardwareState>(DoState);
        ResetCommand = new RelayCommand(DoReset);
    }
    public override void Init()
    {
        if (_initialized) return;
        foreach (var definition in HardwareCatalog.Create()) Items.Add(new HardwareDemo(definition));
        Groups.Add(new HardwareGroup("all"));
        foreach (string group in Items.Select(i => i.Definition.Group).Distinct()) Groups.Add(new HardwareGroup(group));
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = item => item is HardwareDemo demo && (Group is null || Group.Key == "all" || demo.Definition.Group == Group.Key)
            && (string.IsNullOrWhiteSpace(Query) || demo.Definition.Title.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase)
                || demo.Definition.Kind.ToString().Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase));
        _initialized = true; Group = Groups[0]; Selected = Items.First(i => i.Definition.Kind == FluidHardwareKind.FlowMeter);
        OnPropertyChanged(nameof(ItemsView)); OnPropertyChanged(nameof(Count));
    }
    private void RefreshFilter() { if (_initialized) ItemsView.Refresh(); }
    private void DoSelect(HardwareDemo? item) { if (item is not null) Selected = item; }
    private void DoState(HardwareState state)
    {
        if (Selected is not { } item) return;
        if (item.IsDi && state == HardwareState.Active) state = HardwareState.Fault;
        if (state == HardwareState.Unknown) item.Reading = null;
        else if (item.Definition.HasReading && item.Reading is null) item.Reading = item.Definition.DefaultValue;
        item.State = state;
    }
    private void DoReset()
    {
        if (Selected is not { } item) return;
        item.Reading = item.Definition.HasReading ? item.Definition.DefaultValue : null;
        item.Target = item.Definition.HasTarget ? item.Definition.DefaultValue : null;
        item.Unit = item.Definition.DefaultUnit; item.State = HardwareState.Stopped;
        item.SymbolWidth = 240; item.SymbolHeight = 150; item.X = 20; item.Y = 30; item.Angle = 0;
    }
    public void MoveSelected(Vector delta)
    {
        if (Selected is not { } item) return;
        item.X += delta.X; item.Y += delta.Y;
    }
}
