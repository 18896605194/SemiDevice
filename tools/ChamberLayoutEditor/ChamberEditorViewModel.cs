using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using xyz.Client.DataModels.ViewModels;

namespace ChamberLayoutEditor;

public sealed class ChamberEditorViewModel : BaseViewModel
{
    #region Column
    private ChamberLayoutDocument? _document;
    public ChamberLayoutDocument? Document { get => _document; private set => SetProperty(ref _document, value); }
    public ObservableCollection<ChamberPlacement> Parts { get; } = [];
    private ChamberPlacement? _selected;
    public ChamberPlacement? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) SelectionChanged?.Invoke(); } }
    private bool _isPreview;
    public bool IsPreview { get => _isPreview; set { if (SetProperty(ref _isPreview, value)) { OnPropertyChanged(nameof(IsEditing)); PreviewChanged?.Invoke(); } } }
    public bool IsEditing => !IsPreview;
    private double _reach;
    public double Reach { get => _reach; set { if (SetProperty(ref _reach, value)) PreviewChanged?.Invoke(); } }
    private bool _lift;
    public bool Lift { get => _lift; set { if (SetProperty(ref _lift, value)) PreviewChanged?.Invoke(); } }
    private bool _spray;
    public bool Spray { get => _spray; set { if (SetProperty(ref _spray, value)) PreviewChanged?.Invoke(); } }
    private bool _spin;
    public bool Spin { get => _spin; set { if (SetProperty(ref _spin, value)) PreviewChanged?.Invoke(); } }
    private bool _snap = true;
    public bool Snap { get => _snap; set => SetProperty(ref _snap, value); }
    private bool _isDirty;
    public bool IsDirty { get => _isDirty; private set => SetProperty(ref _isDirty, value); }
    private string _error = "";
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    #endregion
    #region Command
    public IRelayCommand UndoCommand { get; }
    public IRelayCommand RedoCommand { get; }
    public IRelayCommand RotateCommand { get; }
    public IRelayCommand HomeCommand { get; }
    public IRelayCommand CenterCommand { get; }
    #endregion
    #region Service
    private readonly List<string> _history = [];
    private readonly Stack<string> _redo = [];
    private string _saved = "";
    private string? _dragStart;
    #endregion

    public ChamberEditorViewModel()
    {
        UndoCommand = new RelayCommand(DoUndo, () => _history.Count > 1);
        RedoCommand = new RelayCommand(DoRedo, () => _redo.Count > 0);
        RotateCommand = new RelayCommand(DoRotate);
        HomeCommand = new RelayCommand(() => Reach = 0);
        CenterCommand = new RelayCommand(() => Reach = 1);
    }

    public event Action? DocumentChanged;
    public event Action? PlacementChanged;
    public event Action? SelectionChanged;
    public event Action? PreviewChanged;
    public bool IsDragging => _dragStart is not null;

    public override void Init() => Load(ChamberLayoutDocument.CreateDefault());

    public void Load(ChamberLayoutDocument document)
    {
        document.Validate();
        _dragStart = null;
        Replace(document);
        _history.Clear();
        _history.Add(document.ToXml());
        _redo.Clear();
        MarkSaved();
        RefreshCommands();
    }

    public void MarkSaved() { _saved = Document?.ToXml() ?? ""; IsDirty = false; }
    public void BeginDrag() { if (!IsPreview) _dragStart = Document?.ToXml(); }
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
    }

    public string PrepareSave()
    {
        EndDrag();
        Document!.Validate();
        return Document.ToXml();
    }

    private void Replace(ChamberLayoutDocument document)
    {
        string? selectedId = Selected?.Id;
        foreach (var part in Parts) part.PropertyChanged -= OnPartChanged;
        Parts.Clear();
        Document = document;
        foreach (var part in document.Parts) { part.PropertyChanged += OnPartChanged; Parts.Add(part); }
        Selected = Parts.FirstOrDefault(p => p.Id == selectedId) ?? Parts.FirstOrDefault(p => p.IsArm) ?? Parts.FirstOrDefault();
        Error = "";
        DocumentChanged?.Invoke();
    }

    private void OnPartChanged(object? sender, PropertyChangedEventArgs e)
    {
        PlacementChanged?.Invoke();
        if (!IsDragging) Commit();
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
    private void RefreshCommands() { UndoCommand.NotifyCanExecuteChanged(); RedoCommand.NotifyCanExecuteChanged(); }
}
