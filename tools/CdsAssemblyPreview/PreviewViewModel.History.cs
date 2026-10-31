using xyz.Client.Presentation.Localization;

namespace CdsAssemblyPreview;

public sealed partial class PreviewViewModel
{
    #region Service
    private const int HistoryLimit = 100;
    private readonly List<LayoutHistory> _history = [];
    private readonly Stack<LayoutHistory> _redo = [];
    private bool _restoringHistory, _replacingLayout, _editingGesture;
    #endregion

    private sealed record LayoutHistory(CdsLayout Layout, string Xml, string? NodeId, string PipeId, string Source);

    // 撤销只保存布局，设备清单及模拟反馈始终使用当前会话的数据。
    private static CdsLayout CopyLayout(CdsLayout source)
    {
        var copy = new CdsLayout { ComponentRoot = source.ComponentRoot, Width = source.Width, Height = source.Height };
        copy.Nodes.AddRange(source.Nodes);
        copy.Pipes.AddRange(source.Pipes.Select(p => p with { Bends = p.Bends.ToArray() }));
        return copy;
    }
    private void RecordHistory()
    {
        if (_restoringHistory || _editingGesture) return;
        string xml = Layout.ToXml();
        if (_history.LastOrDefault()?.Xml == xml) return;
        _history.Add(new(CopyLayout(Layout), xml, Selected?.Id, SelectedPipeId, LayoutSource));
        if (_history.Count > HistoryLimit + 1) _history.RemoveAt(0);
        _redo.Clear(); RefreshHistoryCommands();
    }
    private void RememberHistorySelection()
    {
        if (_restoringHistory || _replacingLayout || _editingGesture || _history.Count == 0 || _history[^1].Xml != Layout.ToXml()) return;
        _history[^1] = _history[^1] with { NodeId = Selected?.Id, PipeId = SelectedPipeId };
    }
    private void ResetHistory()
    {
        _editingGesture = false; _history.Clear(); _redo.Clear();
        RefreshHistoryCommands();
    }
    public void BeginGesture()
    {
        EndGesture(); RememberHistorySelection(); _editingGesture = true;
    }
    public void EndGesture()
    {
        if (!_editingGesture) return;
        _editingGesture = false; RecordHistory();
    }
    private void DoUndo()
    {
        EndGesture();
        if (_history.Count < 2) return;
        _redo.Push(_history[^1]); _history.RemoveAt(_history.Count - 1);
        RestoreHistory(_history[^1]);
        Status = L10n.Get("cdsp.undo.done");
    }
    private void DoRedo()
    {
        EndGesture();
        if (_redo.Count == 0) return;
        var entry = _redo.Pop(); _history.Add(entry); RestoreHistory(entry);
        Status = L10n.Get("cdsp.redo.done");
    }
    private void RestoreHistory(LayoutHistory entry)
    {
        _restoringHistory = true;
        try
        {
            ConnectMode = false;
            LayoutSource = entry.Source;
            ReplaceLayout(CopyLayout(entry.Layout), entry.NodeId, entry.PipeId);
        }
        finally { _restoringHistory = false; RefreshHistoryCommands(); }
    }
    private void RefreshHistoryCommands()
    {
        UndoCommand.NotifyCanExecuteChanged(); RedoCommand.NotifyCanExecuteChanged();
    }
}
