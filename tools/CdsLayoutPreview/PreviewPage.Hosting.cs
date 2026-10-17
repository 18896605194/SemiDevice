using System.IO;
using System.Windows;

namespace CdsLayoutPreview;

public partial class PreviewPage
{
    private bool _embedded;
    public event Action? DocumentChanged;
    public event Action? SaveRequested;
    public bool HasUnsavedChanges => IsDirty || HasDraft || ViewModel.PipeProperties.HasPendingChanges;

    public void ConfigureEmbedded()
    {
        _embedded = true;
        EditorRoot.Margin = new Thickness(8);
        EditorHeading.Visibility = Visibility.Collapsed;
        FileActions.Visibility = Visibility.Collapsed;
        _history.Changed += () => DocumentChanged?.Invoke();
        ViewModel.PipeProperties.PropertyChanged += (_, _) => DocumentChanged?.Invoke();
        Unloaded += (_, _) => PauseAnimation();
        Loaded += (_, _) =>
        {
            foreach (var pipe in Document.Pipes)
            {
                _pipes[pipe.Id].IsAnimationEnabled = pipe.Appearance.IsAnimationEnabled;
            }
            Refresh(false);
        };
    }

    public string PrepareSave()
    {
        PointerUp();
        if (HasDraft)
        {
            throw new InvalidDataException("请完成管路连接或按 Esc 取消草稿，再保存。");
        }
        if (!ApplyPipeProperties())
        {
            throw new InvalidDataException(ViewModel.PipeProperties.Error);
        }
        Document.Validate();
        return Document.ToXml();
    }

    public void AcceptSave(string path)
    {
        _filePath = path;
        _history.MarkSaved();
        UpdateProperties(false);
        Status("布局已保存。拖动槽或接口调整位置，选中管路编辑文字和属性。");
    }
}
