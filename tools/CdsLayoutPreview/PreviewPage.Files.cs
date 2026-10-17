using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace CdsLayoutPreview;

public partial class PreviewPage
{
    internal void SaveTo(string path)
    {
        PointerUp();
        if (!ApplyPipeProperties())
        {
            throw new InvalidDataException(ViewModel.PipeProperties.Error);
        }
        Document.Save(path);
        _filePath = Path.GetFullPath(path);
        _history.MarkSaved();
        UpdateProperties(false);
        Status("已保存：" + _filePath);
    }

    internal void LoadFrom(string path)
    {
        // 先完整解析校验，失败时保留当前布局和历史。
        var loaded = LayoutDocument.Load(path);
        CancelGesture();
        _history.Load(loaded);
        _filePath = Path.GetFullPath(path);
        _selection = null;
        _bend = -1;
        Refresh();
        Status("已加载：" + _filePath);
    }

    private bool SaveInteractive(bool saveAs)
    {
        if (HasDraft)
        {
            Status("请先连接终点完成管路，或按 Esc 取消草稿，再保存。");
            return false;
        }
        try
        {
            string? path = _filePath;
            if (saveAs || path is null)
            {
                var dialog = new SaveFileDialog { Filter = "CDS 布局 (*.xml)|*.xml", FileName = path is null ? "CdsLayout.xml" : Path.GetFileName(path), DefaultExt = ".xml", AddExtension = true };
                if (path is not null)
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(path);
                }
                if (dialog.ShowDialog(Window.GetWindow(this)) != true)
                {
                    return false;
                }
                path = dialog.FileName;
            }
            SaveTo(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException)
        {
            ShowError("保存失败", exception);
            return false;
        }
    }

    public bool ConfirmDiscardChanges()
    {
        PointerUp();
        if (!_history.IsDirty && !HasDraft && !ViewModel.PipeProperties.HasPendingChanges)
        {
            return true;
        }
        if (HasDraft)
        {
            var draftChoice = MessageBox.Show(Window.GetWindow(this), "有尚未连接终点的管路草稿。放弃草稿并继续？", "未完成的管路", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (draftChoice != MessageBoxResult.Yes)
            {
                return false;
            }
            CancelGesture();
        }
        if (!_history.IsDirty && !ViewModel.PipeProperties.HasPendingChanges)
        {
            return true;
        }
        var choice = MessageBox.Show(Window.GetWindow(this), "当前布局有未保存的修改。是否先保存？", "未保存的布局", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return choice == MessageBoxResult.No || (choice == MessageBoxResult.Yes && SaveInteractive(false));
    }

    private void OpenInteractive()
    {
        var dialog = new OpenFileDialog { Filter = "CDS 布局 (*.xml)|*.xml", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true || !ConfirmDiscardChanges())
        {
            return;
        }
        TryLoad(dialog.FileName);
    }

    private void TryLoad(string path)
    {
        try
        {
            LoadFrom(path);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException or InvalidOperationException)
        {
            ShowError("加载失败，保留原布局", exception);
        }
    }

    private void OnOpen(object sender, RoutedEventArgs args) => OpenInteractive();
    private void OnSave(object sender, RoutedEventArgs args) => SaveInteractive(false);
    private void OnSaveAs(object sender, RoutedEventArgs args) => SaveInteractive(true);

    private void OnReload(object sender, RoutedEventArgs args)
    {
        if (_filePath is null)
        {
            Status("当前是示例布局，请先保存或打开一份布局文件。");
            return;
        }
        string path = _filePath;
        if (ConfirmDiscardChanges())
        {
            TryLoad(path);
        }
    }

    private void ShowError(string title, Exception exception)
    {
        Status(title + "：" + exception.Message);
        MessageBox.Show(Window.GetWindow(this), exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
