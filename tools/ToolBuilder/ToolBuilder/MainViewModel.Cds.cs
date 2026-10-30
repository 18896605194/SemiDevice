using System.Security.Cryptography;
using System.Xml.Linq;
using ToolBuilder.Core;

namespace ToolBuilder;

public sealed partial class MainViewModel
{
    private readonly Dictionary<XElement, CdsLayoutViewModel> _layouts = [];
    private readonly Dictionary<XElement, string> _layoutErrors = [];
    private readonly Dictionary<string, FileWrite> _layoutFiles = new(StringComparer.OrdinalIgnoreCase);
    private CdsLayoutViewModel? _currentCdsLayout;
    private string _layoutError = "";
    public CdsLayoutViewModel? CurrentCdsLayout { get => _currentCdsLayout; private set => Set(ref _currentCdsLayout, value); }
    public string LayoutError { get => _layoutError; private set => Set(ref _layoutError, value); }
    public bool CanEditCdsLayout => CdsLayoutViewModel.Supports(Selected);
    public bool HasLayoutChanges => _layouts.Values.Any(layout => layout.IsDirty) || HasChamberChanges;

    private void ValidateCdsLayouts()
    {
        foreach (var pair in _layouts)
        {
            try
            {
                pair.Value.Validate();
            }
            catch (Exception exception)
            {
                Issues.Add(new ValidationIssue("错误", ConfigDocument.PathOf(pair.Key), "管路布局：" + exception.Message));
            }
        }
        foreach (var error in _layoutErrors)
        {
            if (_document is not null && error.Key.Ancestors().Contains(_document.Xml.Root!))
            {
                Issues.Add(new ValidationIssue("错误", ConfigDocument.PathOf(error.Key), error.Value));
            }
        }
    }

    private void ResetCdsLayouts(string machinePath)
    {
        // 先读取新目录；读取失败时不丢弃正在编辑的布局。
        var files = new Dictionary<string, FileWrite>(StringComparer.OrdinalIgnoreCase);
        string folder = Path.GetDirectoryName(CdsLayoutViewModel.LayoutPath(machinePath, "placeholder"))!;
        if (Directory.Exists(folder))
        {
            foreach (string path in Directory.EnumerateFiles(folder, "*.xml"))
            {
                byte[] bytes = File.ReadAllBytes(path);
                files[path] = new FileWrite(path, bytes, SHA256.HashData(bytes));
            }
        }
        foreach (var layout in _layouts.Values)
        {
            layout.Editor.PauseAnimation();
        }
        _layouts.Clear();
        _layoutErrors.Clear();
        _layoutFiles.Clear();
        foreach (var pair in files)
        {
            _layoutFiles.Add(pair.Key, pair.Value);
        }
        CurrentCdsLayout = null;
        LayoutError = "";
    }

    private void OpenCdsLayout()
    {
        if (!CanEditCdsLayout || Selected is null || _document is null)
        {
            CurrentCdsLayout = null;
            return;
        }
        try
        {
            if (!_layouts.TryGetValue(Selected.Element, out var layout))
            {
                layout = new CdsLayoutViewModel(Selected, _document.FilePath);
                layout.Changed += () => Raise(nameof(Title));
                layout.Editor.SaveRequested += () => SaveCommand.Execute(null);
                _layouts.Add(Selected.Element, layout);
            }
            CurrentCdsLayout = layout;
            _layoutErrors.Remove(Selected.Element);
            LayoutError = "";
            Raise(nameof(Title));
        }
        catch (Exception exception)
        {
            CurrentCdsLayout = null;
            LayoutError = "布局加载失败，原文件未修改：" + exception.Message;
            _layoutErrors[Selected.Element] = LayoutError;
            Status = LayoutError;
        }
    }

    internal void SaveWorkspace(string target)
    {
        if (_document is null)
        {
            throw new InvalidOperationException("请先打开 machine.xml。");
        }
        // 先规范成完整路径（短文件名展开、去掉 . 和 ..），跟文档、布局记下的写法一致；不然同一个布局文件会被当成"目标已存在"。
        target = Path.GetFullPath(target);
        bool moving = !string.Equals(target, _document.FilePath, StringComparison.OrdinalIgnoreCase);
        var machineWrite = _document.PrepareWrite(target);
        var layouts = new List<(CdsLayoutViewModel Layout, FileWrite Write)>();
        foreach (var pair in _layouts)
        {
            if (!pair.Key.Ancestors().Contains(_document.Xml.Root!))
            {
                if (pair.Value.IsDirty)
                {
                    throw new InvalidDataException("已删除的元件还有未保存布局，请先撤回删除，或重新打开配置。");
                }
                continue;
            }
            layouts.Add((pair.Value, pair.Value.PrepareWrite(target)));
        }
        var writes = new List<FileWrite> { machineWrite };
        writes.AddRange(layouts.Select(item => item.Write));
        var chamberWrites = PrepareChamberWrites(target);
        writes.AddRange(chamberWrites.Select(item => item.Write));
        if (moving)
        {
            AppendUnopenedChamberWrites(writes, target);
            // 另存为也携带没有打开过的布局，防止只复制 machine.xml 而漏掉关联页面。
            string folder = Path.GetDirectoryName(CdsLayoutViewModel.LayoutPath(target, "placeholder"))!;
            foreach (var original in _layoutFiles.Values)
            {
                string destination = Path.Combine(folder, Path.GetFileName(original.Path));
                if (writes.Any(write => string.Equals(write.Path, destination, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                if (!File.Exists(original.Path) || !SHA256.HashData(File.ReadAllBytes(original.Path)).SequenceEqual(original.ExpectedHash!))
                {
                    throw new IOException("布局已被外部修改，请重新打开后另存为：" + original.Path);
                }
                writes.Add(new FileWrite(destination, original.Content, null));
            }
        }
        WorkspaceSave.Write(writes);
        AcceptChamberWrites(chamberWrites, moving, writes);
        _document.AcceptSave(machineWrite);
        foreach (var item in layouts)
        {
            item.Layout.AcceptSave(item.Write);
        }
        if (moving)
        {
            _layoutFiles.Clear();
        }
        string cdsFolder = Path.GetDirectoryName(CdsLayoutViewModel.LayoutPath(target, "placeholder"))! + Path.DirectorySeparatorChar;
        foreach (var write in writes.Skip(1).Where(write => write.Path.StartsWith(cdsFolder, StringComparison.OrdinalIgnoreCase)))
        {
            _layoutFiles[write.Path] = write with { ExpectedHash = SHA256.HashData(write.Content) };
        }
        Raise(nameof(Title));
    }
}
