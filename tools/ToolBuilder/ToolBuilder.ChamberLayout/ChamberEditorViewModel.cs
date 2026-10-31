using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Input;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;

namespace ChamberLayoutEditor;

public sealed class ChamberEditorViewModel : BaseViewModel
{
    #region Column
    private ChamberLayoutDocument? _document;
    public ChamberLayoutDocument? Document { get => _document; private set => SetProperty(ref _document, value); }
    public ObservableCollection<ChamberPlacement> Parts { get; } = [];
    public ObservableCollection<ChamberLayoutDocument> Chambers { get; } = [];
    private ChamberLayoutDocument? _selectedChamber;
    public ChamberLayoutDocument? SelectedChamber
    {
        get => _selectedChamber;
        set
        {
            if (_changingChamber || value is null || ReferenceEquals(_selectedChamber, value) || !Chambers.Contains(value)) return;
            CaptureChamber();
            SetProperty(ref _selectedChamber, value);
            RestoreChamber(_chambers[value.ComponentRoot]);
        }
    }
    public bool HasConfiguration => Document is not null;
    public bool NeedsConfiguration => !HasConfiguration;
    public bool HasUnsavedChanges => IsDirty || IsDragging
        || _chambers.Any(pair => pair.Key != SelectedChamber?.ComponentRoot && pair.Value.Xml != pair.Value.SavedXml);
    private ChamberPlacement? _selected;
    public ChamberPlacement? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            OnPropertyChanged(nameof(SelectedMotion));
            OnPropertyChanged(nameof(CanMoveArm)); OnPropertyChanged(nameof(CanCenterArm)); OnPropertyChanged(nameof(CanRaise));
            OnPropertyChanged(nameof(CanSpray)); OnPropertyChanged(nameof(CanSpin));
            OnPropertyChanged(nameof(CanOpenDoor)); OnPropertyChanged(nameof(HasMotion));
            if (!HasMotion && IsPreview) SelectedInspectorTab = 0;
            RefreshCommands();
            SelectionChanged?.Invoke();
        }
    }
    public ChamberMotionState? SelectedMotion => Selected is { } part ? MotionFor(part.Id) : null;
    public bool CanMoveArm => Selected?.IsArm == true;
    public bool CanCenterArm => CanMoveArm && Document?.Parts.Any(p => p.Kind == "Disk") == true;
    public bool CanRaise => Selected?.Kind is "Lift" or "Bowl";
    public bool CanSpray => Selected?.Kind == "Nozzle";
    public bool CanSpin => Selected?.Kind == "Disk";
    public bool CanOpenDoor => Selected?.Kind == "Door";
    public bool HasMotion => CanMoveArm || CanRaise || CanSpray || CanSpin || CanOpenDoor;
    public bool IsPreview => SelectedInspectorTab == 2;
    public bool IsEditing => HasConfiguration && !IsPreview;
    private int _selectedInspectorTab;
    public int SelectedInspectorTab
    {
        get => _selectedInspectorTab;
        set
        {
            value = Math.Clamp(value, 0, 2);
            if (!SetProperty(ref _selectedInspectorTab, value == 2 && !HasMotion ? 0 : value)) return;
            OnPropertyChanged(nameof(IsPreview)); OnPropertyChanged(nameof(IsEditing));
            RefreshCommands();
            PreviewChanged?.Invoke();
        }
    }
    private bool _snap = true;
    public bool Snap { get => _snap; set => SetProperty(ref _snap, value); }
    private bool _isDirty;
    public bool IsDirty { get => _isDirty; private set => SetProperty(ref _isDirty, value); }
    private string _error = "";
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public string Warning
    {
        get
        {
            if (Document?.Parts.FirstOrDefault(p => p.Kind == "Disk") is not { } disk) return "";
            return string.Join(Environment.NewLine, Parts.Where(p => p.IsArm && !p.AutoLength)
                .Where(p => Math.Abs(p.Size("ArmLength") - Math.Sqrt(Math.Pow(p.X - disk.X, 2) + Math.Pow(p.Z - disk.Z, 2))) > 0.01)
                .Select(p => L10n.Get("chambereditor.manual_length_warning", p.Id)));
        }
    }
    #endregion
    #region Command
    public IRelayCommand UndoCommand { get; }
    public IRelayCommand RedoCommand { get; }
    public IRelayCommand RotateCommand { get; }
    public IRelayCommand HomeCommand { get; }
    public IRelayCommand CenterCommand { get; }
    public IRelayCommand RaiseCommand { get; }
    public IRelayCommand LowerCommand { get; }
    public IRelayCommand OpenDoorCommand { get; }
    public IRelayCommand CloseDoorCommand { get; }
    #endregion
    #region Service
    private readonly List<string> _history = [];
    private readonly Stack<string> _redo = [];
    private string _saved = "";
    private string? _dragStart;
    private readonly Dictionary<string, ChamberMotionState> _motions = [];
    private readonly Dictionary<string, ChamberLayoutSession> _chambers = [];
    private bool _changingChamber;
    #endregion

    public ChamberEditorViewModel()
    {
        UndoCommand = new RelayCommand(DoUndo, () => _history.Count > 1);
        RedoCommand = new RelayCommand(DoRedo, () => _redo.Count > 0);
        RotateCommand = new RelayCommand(DoRotate, () => IsEditing && Selected is not null);
        HomeCommand = new RelayCommand(DoHome, () => IsPreview && CanMoveArm);
        CenterCommand = new RelayCommand(DoCenter, () => IsPreview && CanCenterArm);
        RaiseCommand = new RelayCommand(DoRaise, () => IsPreview && CanRaise);
        LowerCommand = new RelayCommand(DoLower, () => IsPreview && CanRaise);
        OpenDoorCommand = new RelayCommand(DoOpenDoor, () => IsPreview && CanOpenDoor);
        CloseDoorCommand = new RelayCommand(DoCloseDoor, () => IsPreview && CanOpenDoor);
    }

    public event Action? DocumentChanged;
    public event Action? PlacementChanged;
    public event Action? SelectionChanged;
    public event Action? PreviewChanged;
    public bool IsDragging => _dragStart is not null;

    public override void Init()
    {
        // 设备组成由宿主或 machine.xml 提供，启动时不创建示例设备。
    }

    public void Clear()
    {
        foreach (var part in Parts)
        {
            part.PropertyChanged -= OnPartChanged;
            foreach (var size in part.Dimensions) size.PropertyChanged -= OnDimensionChanged;
        }
        foreach (var state in _motions.Values) state.PropertyChanged -= OnMotionChanged;
        _motions.Clear(); _chambers.Clear(); _changingChamber = true;
        Chambers.Clear(); _selectedChamber = null; _changingChamber = false;
        if (Document is not null) Document.PropertyChanged -= OnPartChanged;
        _dragStart = null; Parts.Clear(); Selected = null; Document = null; _history.Clear(); _redo.Clear(); _saved = ""; IsDirty = false;
        OnPropertyChanged(nameof(SelectedChamber)); OnPropertyChanged(nameof(HasConfiguration)); OnPropertyChanged(nameof(NeedsConfiguration));
        OnPropertyChanged(nameof(IsEditing)); DocumentChanged?.Invoke(); RefreshCommands();
    }

    public void Load(ChamberLayoutDocument document)
    {
        document.Validate();
        _changingChamber = true;
        Chambers.Clear(); _chambers.Clear();
        _selectedChamber = null;
        OnPropertyChanged(nameof(SelectedChamber));
        _changingChamber = false;
        _dragStart = null;
        SelectedInspectorTab = 0;
        foreach (var motion in _motions.Values) motion.PropertyChanged -= OnMotionChanged;
        _motions.Clear();
        Replace(document);
        OnPropertyChanged(nameof(SelectedMotion));
        _history.Clear();
        _history.Add(document.ToXml());
        _redo.Clear();
        MarkSaved();
        RefreshCommands();
    }

    public void LoadChambers(IReadOnlyList<ChamberLayoutDocument> configurations)
    {
        if (configurations.Count == 0 || configurations.Select(d => d.ComponentRoot).Distinct().Count() != configurations.Count)
            throw new InvalidDataException(L10n.Get("chambereditor.invalid_layout"));
        foreach (var configuration in configurations) configuration.Validate();
        _changingChamber = true;
        Chambers.Clear(); _chambers.Clear(); _selectedChamber = null;
        foreach (var configuration in configurations)
        {
            var copy = ChamberLayoutDocument.Parse(configuration.ToXml());
            _chambers.Add(copy.ComponentRoot, new ChamberLayoutSession(copy));
            Chambers.Add(copy);
        }
        _changingChamber = false;
        SelectedChamber = Chambers[0];
    }

    public void ApplyLayouts(string xml)
    {
        if (Chambers.Count == 0) throw new InvalidDataException(L10n.Get("chambereditor.machine_required"));
        CaptureChamber();
        var root = ChamberLayoutDocument.ReadXml(xml).Root;
        var elements = root?.Name == "ChamberLayout" ? new[] { root } : root?.Name == "ChamberLayouts"
            && (string?)root.Attribute("Version") == "1" ? root.Elements("ChamberLayout").ToArray() : [];
        if (elements.Length == 0) throw new InvalidDataException(L10n.Get("chambereditor.invalid_format"));
        var changes = new Dictionary<string, string>();
        foreach (var element in elements)
        {
            var layout = ChamberLayoutDocument.Parse(element.ToString());
            if (!_chambers.TryGetValue(layout.ComponentRoot, out var session) || changes.ContainsKey(layout.ComponentRoot))
                throw new InvalidDataException(L10n.Get("chambereditor.layout_chamber_mismatch", layout.ComponentRoot));
            changes.Add(layout.ComponentRoot, session.Configuration.WithLayout(layout).ToXml());
        }
        foreach (var (name, snapshot) in changes)
        {
            var session = _chambers[name];
            session.Xml = session.SavedXml = snapshot;
            session.History.Clear(); session.History.Add(snapshot); session.Redo.Clear();
        }
        RestoreChamber(_chambers[SelectedChamber!.ComponentRoot]);
    }

    public string PrepareWorkspaceSave()
    {
        if (Chambers.Count == 0) return PrepareSave();
        CaptureChamber();
        return new XDocument(new XElement("ChamberLayouts", new XAttribute("Version", "1"),
            Chambers.Select(chamber => XElement.Parse(ChamberLayoutDocument.Parse(_chambers[chamber.ComponentRoot].Xml).ToXml())))).ToString();
    }

    public void MarkWorkspaceSaved()
    {
        CaptureChamber();
        foreach (var session in _chambers.Values) session.SavedXml = session.Xml;
        MarkSaved();
    }

    private void CaptureChamber()
    {
        EndDrag();
        if (SelectedChamber is null || Document is null || !_chambers.TryGetValue(SelectedChamber.ComponentRoot, out var session)) return;
        session.Xml = Document.ToXml(); session.SavedXml = _saved; session.SelectedId = Selected?.Id;
        session.History.Clear(); session.History.AddRange(_history);
        session.Redo.Clear(); foreach (string item in _redo.Reverse()) session.Redo.Push(item);
    }

    private void RestoreChamber(ChamberLayoutSession session)
    {
        _changingChamber = true;
        _dragStart = null; SelectedInspectorTab = 0;
        foreach (var motion in _motions.Values) motion.PropertyChanged -= OnMotionChanged;
        _motions.Clear();
        Replace(ChamberLayoutDocument.ParseSnapshot(session.Xml));
        if (session.SelectedId is { } id) Selected = Parts.FirstOrDefault(p => p.Id == id) ?? Selected;
        _history.Clear(); _history.AddRange(session.History);
        _redo.Clear(); foreach (string item in session.Redo.Reverse()) _redo.Push(item);
        _saved = session.SavedXml; IsDirty = session.Xml != _saved;
        try { Document!.Validate(); Error = ""; } catch (InvalidDataException ex) { Error = ex.Message; }
        _changingChamber = false;
        OnPropertyChanged(nameof(SelectedMotion));
        RefreshCommands();
    }

    public void MarkSaved()
    {
        _saved = Document?.ToXml() ?? ""; IsDirty = false;
        if (SelectedChamber is { } chamber && _chambers.TryGetValue(chamber.ComponentRoot, out var session)) session.Xml = session.SavedXml = _saved;
    }
    public void BeginDrag() { if (!IsPreview) { _dragStart = Document?.ToXml(); RefreshCommands(); } }
    public void EndDrag()
    {
        if (_dragStart is null) return;
        _dragStart = null;
        Commit();
        PlacementChanged?.Invoke();
    }
    public void CancelDrag()
    {
        if (_dragStart is not { } before) return;
        _dragStart = null;
        Replace(ChamberLayoutDocument.Parse(before));
        IsDirty = before != _saved;
        RefreshCommands();
    }

    public string PrepareSave()
    {
        EndDrag();
        if (Document is null) throw new InvalidDataException(L10n.Get("chambereditor.machine_required"));
        Document.Validate();
        return Document.ToXml();
    }

    public void ImportXaml(string xaml)
    {
        EndDrag();
        var imported = ChamberLayoutDocument.Parse(ChamberDiagramXaml.ReadLayout(xaml).ToXml());
        if (Document is null || imported.ComponentRoot != Document.ComponentRoot)
            throw new InvalidDataException(L10n.Get("chambereditor.layout_chamber_mismatch", imported.ComponentRoot));
        Replace(Document.WithLayout(imported)); Commit();
    }

    private void Replace(ChamberLayoutDocument document)
    {
        string? selectedId = Selected?.Id;
        var ids = document.Parts.Select(p => p.Id).ToHashSet();
        foreach (string id in _motions.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            _motions[id].PropertyChanged -= OnMotionChanged;
            _motions.Remove(id);
        }
        if (Document is not null) Document.PropertyChanged -= OnPartChanged;
        foreach (var part in Parts)
        {
            part.PropertyChanged -= OnPartChanged;
            foreach (var dimension in part.Dimensions) dimension.PropertyChanged -= OnDimensionChanged;
        }
        Parts.Clear();
        Document = document;
        OnPropertyChanged(nameof(HasConfiguration)); OnPropertyChanged(nameof(NeedsConfiguration));
        OnPropertyChanged(nameof(IsEditing)); OnPropertyChanged(nameof(CanCenterArm));
        document.PropertyChanged += OnPartChanged;
        foreach (var part in document.Parts)
        {
            part.PropertyChanged += OnPartChanged;
            foreach (var dimension in part.Dimensions) dimension.PropertyChanged += OnDimensionChanged;
            Parts.Add(part);
        }
        Selected = Parts.FirstOrDefault(p => p.Id == selectedId) ?? Parts.FirstOrDefault(p => p.IsArm) ?? Parts.FirstOrDefault();
        Error = "";
        OnPropertyChanged(nameof(Warning));
        DocumentChanged?.Invoke();
    }

    private void OnPartChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Warning));
        PlacementChanged?.Invoke();
        if (!IsDragging) Commit();
    }
    private void OnDimensionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChamberDimension.Value)) OnPartChanged(sender, e);
    }
    private void Commit()
    {
        string snapshot = Document!.ToXml();
        if (_history.Count == 0 || snapshot != _history[^1])
        {
            _history.Add(snapshot);
            if (_history.Count > 61) _history.RemoveAt(0);
            _redo.Clear();
        }
        try { Document.Validate(); Error = ""; }
        catch (InvalidDataException ex) { Error = ex.Message; }
        IsDirty = snapshot != _saved;
        RefreshCommands();
    }
    private void DoUndo()
    {
        CancelDrag();
        _redo.Push(_history[^1]);
        _history.RemoveAt(_history.Count - 1);
        Restore(_history[^1]);
    }
    private void DoRedo() { CancelDrag(); _history.Add(_redo.Pop()); Restore(_history[^1]); }
    private void Restore(string xml)
    {
        Replace(ChamberLayoutDocument.ParseSnapshot(xml));
        try { Document!.Validate(); } catch (InvalidDataException ex) { Error = ex.Message; }
        IsDirty = xml != _saved; RefreshCommands();
    }
    private void DoRotate() { if (Selected is { } part && !IsPreview) part.Yaw = (part.Yaw + 90) % 360; }
    private void DoHome() { if (IsPreview && CanMoveArm) SelectedMotion!.Reach = 0; }
    private void DoCenter() { if (IsPreview && CanCenterArm) SelectedMotion!.Reach = 1; }
    private void DoRaise() { if (IsPreview && CanRaise) SelectedMotion!.Raised = true; }
    private void DoLower() { if (IsPreview && CanRaise) SelectedMotion!.Raised = false; }
    private void DoOpenDoor() { if (IsPreview && CanOpenDoor) SelectedMotion!.Open = true; }
    private void DoCloseDoor() { if (IsPreview && CanOpenDoor) SelectedMotion!.Open = false; }

    internal ChamberMotionState MotionFor(string id)
    {
        if (_motions.TryGetValue(id, out var motion)) return motion;
        motion = new ChamberMotionState();
        motion.PropertyChanged += OnMotionChanged;
        _motions.Add(id, motion);
        return motion;
    }

    private void OnMotionChanged(object? sender, PropertyChangedEventArgs e) => PreviewChanged?.Invoke();

    private void RefreshCommands()
    {
        UndoCommand.NotifyCanExecuteChanged(); RedoCommand.NotifyCanExecuteChanged();
        RotateCommand.NotifyCanExecuteChanged(); HomeCommand.NotifyCanExecuteChanged(); CenterCommand.NotifyCanExecuteChanged();
        RaiseCommand.NotifyCanExecuteChanged(); LowerCommand.NotifyCanExecuteChanged();
        OpenDoorCommand.NotifyCanExecuteChanged(); CloseDoorCommand.NotifyCanExecuteChanged();
    }
}
