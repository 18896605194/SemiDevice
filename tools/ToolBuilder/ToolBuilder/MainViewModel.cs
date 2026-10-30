using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Xml.Linq;
using Microsoft.Win32;
using ToolBuilder.Core;

namespace ToolBuilder;

public sealed partial class MainViewModel : Observable
{
    public ObservableCollection<NodeModel> Roots { get; } = [];
    public ObservableCollection<FieldModel> Fields { get; } = [];
    /// <summary>"配方字段"页的行：工艺配方字段表里的字段，一行一个。</summary>
    public ObservableCollection<RecipeFieldModel> RecipeFieldRows { get; } = [];
    public ObservableCollection<AlarmModel> Alarms { get; } = [];
    public ObservableCollection<ValidationIssue> Issues { get; } = [];
    public ObservableCollection<string> Sources { get; } = [];
    public ObservableCollection<ElementSpec> Elements { get; } = [];
    public ObservableCollection<string> RecentFiles { get; } = [];
    private readonly UserPreferences _preferences = UserPreferences.Read();
    private readonly bool _rememberSettings;
    private ConfigDocument? _document;
    private ElementCatalog _catalog = new();
    public ConfigDocument? Document => _document;
    public string Title => $"ToolBuilder{(_document is null ? "" : " · " + Path.GetFileName(_document.FilePath))}{(_document?.IsDirty == true || HasLayoutChanges ? " *" : "")}";
    public string FilePath => _document?.FilePath ?? "尚未打开 machine.xml 文件";
    public string ConfigDirectory => _document is null ? "请选择 machine.xml，元件来源可以单独选择" : Path.GetDirectoryName(_document.FilePath)!;
    public string XmlPreview => _document?.GetText() ?? "打开 machine.xml 后在这里查看完整 XML。";
    public string OriginalPreview => _document?.OriginalText ?? "";
    public string CatalogSummary => $"已识别 {Elements.Count} 个元件 · {Sources.Count} 个来源";
    private string _status = "打开一份 machine.xml 开始配置。";
    public string Status { get => _status; set => Set(ref _status, value); }
    private bool _busy;
    public bool IsBusy { get => _busy; private set { Set(ref _busy, value); CommandManager.InvalidateRequerySuggested(); } }
    private string _selectedSource = "";
    public string SelectedSource { get => _selectedSource; set => Set(ref _selectedSource, value); }
    private string? _selectedRecent;
    public string? SelectedRecent { get => _selectedRecent; set => Set(ref _selectedRecent, value); }
    private NodeModel? _selected;
    public NodeModel? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            bool stayInChamber = EditorPage == "ChamberLayout";
            bool stayInCds = EditorPage == "CdsLayout";
            if (value is not null) value.IsExpanded = true;
            BuildEditor();
            Raise(nameof(SelectionPath)); Raise(nameof(SelectionDescription)); Raise(nameof(AddChildLabel));
            Raise(nameof(CanEditCdsLayout));
            Raise(nameof(CanEditChamberLayout));
            EditorPage = CanEditChamberLayout && (stayInChamber || ReferenceEquals(FindChamber(value?.Element), value?.Element)) ? "ChamberLayout"
                : CanEditCdsLayout && (stayInCds || ReferenceEquals(FindCdsModule(value?.Element), value?.Element)) ? "CdsLayout"
                : CanEditRecipeFields ? "RecipeFields" : CanEditRecipeHead ? "RecipeHead" : "Parameters";
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public FieldModel? SelectedField { get; set; }
    public RecipeFieldModel? SelectedRecipeField { get; set; }
    public AlarmModel? SelectedAlarm { get; set; }
    public string SelectionPath => Selected?.Path ?? "选择左侧节点";
    public string AddChildLabel => Selected is null ? "添加子元件" : $"在 {Selected.Name} 下添加元件";
    public string ParametersSummary => Fields.Count > 0 ? $"当前节点 {Fields.Count} 个 machine.xml 配置项" : Selected?.Type.Length == 0
        ? "此分组没有自身参数，请从左侧选择下级节点进行配置。" : "此元件没有声明 machine.xml 配置项，请从左侧选择需要配置的节点。";
    public bool HasFields => Fields.Count > 0;
    public bool HasAlarmOverrides => Alarms.Count > 0;
    /// <summary>选中的是工艺配方元件下的字段表（Fields）：显示"配方字段"页。</summary>
    public bool CanEditRecipeFields => Selected is not null && RecipeFields.IsFieldsNode(Selected.Element);
    /// <summary>选中的是工艺配方元件下的配方头字段表（Head）：显示"配方头"页。</summary>
    public bool CanEditRecipeHead => Selected is not null && RecipeFields.IsHeadNode(Selected.Element);
    private bool CanEditRecipeTable => CanEditRecipeFields || CanEditRecipeHead;
    /// <summary>腔体配的几种 Type：步骤下拉一种 Type 一列选项（窗口按它生成"配方字段"页的选项列）。</summary>
    public IReadOnlyList<string> RecipeOptionTypes { get; private set; } = [""];
    public IReadOnlyList<RecipeFieldType> RecipeFieldTypes => RecipeFields.Types;
    private string _editorPage = "Parameters";
    public string EditorPage
    {
        get => _editorPage;
        set
        {
            Set(ref _editorPage, value);
            if (value == "CdsLayout" && !_syncingCds)
            {
                OpenCdsLayout();
            }
            if (value == "ChamberLayout" && !_syncingChamber) OpenChamberLayout();
        }
    }
    public string SelectionDescription
    {
        get
        {
            if (Selected is null) return "元件树保留 machine.xml 中的分组和原始顺序。";
            if (Selected.Type.Length == 0) return "无 Type 节点：可能是分组、已有子元件配置或专用数据。";
            return _catalog.Elements.TryGetValue(Selected.Type, out var spec) ? spec.Description
                : "元件类型尚未加载。现有参数可以编辑，请添加对应 DLL 或依赖目录。";
        }
    }
    public string SelectedType
    {
        get => Selected?.Type ?? "";
        set
        {
            if (Selected is null || Selected.Type == value) return;
            Selected.Type = value ?? "";
            Selected.RefreshMetadata(_catalog);
            BuildEditor(); Raise(nameof(SelectionDescription)); Raise();
        }
    }

    public ICommand OpenCommand { get; }
    public ICommand RecentCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand AddDirectoryCommand { get; }
    public ICommand AddDllCommand { get; }
    public ICommand RemoveSourceCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand AddRootCommand { get; }
    public ICommand AddChildCommand { get; }
    public ICommand AddGroupCommand { get; }
    public ICommand AddRootGroupCommand { get; }
    public ICommand ExpandAllCommand { get; }
    public ICommand CollapseAllCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand DownCommand { get; }
    public ICommand AddValueCommand { get; }
    public ICommand ClearValueCommand { get; }
    public ICommand AddRecipeFieldCommand { get; }
    public ICommand DeleteRecipeFieldCommand { get; }
    public ICommand RecipeFieldUpCommand { get; }
    public ICommand RecipeFieldDownCommand { get; }
    public ICommand AddAlarmCommand { get; }
    public ICommand DeleteAlarmCommand { get; }

    public MainViewModel(bool rememberSettings = true)
    {
        _rememberSettings = rememberSettings;
        foreach (var path in _preferences.RecentFiles.Where(File.Exists).Take(12)) RecentFiles.Add(path);
        OpenCommand = new ActionCommand(OpenDialog, () => !IsBusy);
        RecentCommand = new ActionCommand(() => { if (SelectedRecent is not null) OpenSafely(SelectedRecent); }, () => SelectedRecent is not null && !IsBusy);
        SaveCommand = new ActionCommand(() => Save(false), () => _document is not null && !IsBusy);
        SaveAsCommand = new ActionCommand(() => Save(true), () => _document is not null && !IsBusy);
        AddDirectoryCommand = new ActionCommand(AddDirectory, () => !IsBusy);
        AddDllCommand = new ActionCommand(AddDll, () => !IsBusy);
        RemoveSourceCommand = new ActionCommand(() => { Sources.Remove(SelectedSource); _ = LoadCatalogAsync(); }, () => !IsBusy && Sources.Contains(SelectedSource));
        ReloadCommand = new ActionCommand(() => _ = LoadCatalogAsync(), () => !IsBusy);
        ValidateCommand = new ActionCommand(() => { Validate(); EditorPage = "Validation"; }, () => _document is not null && !IsBusy);
        AddRootCommand = new ActionCommand(() => AddElement(null), () => _document is not null && !IsBusy);
        AddChildCommand = new ActionCommand(() => AddElement(Selected), () => Selected is not null && !IsBusy);
        AddGroupCommand = new ActionCommand(() => AddGroup(Selected), () => _document is not null && !IsBusy);
        AddRootGroupCommand = new ActionCommand(() => AddGroup(null), () => _document is not null && !IsBusy);
        ExpandAllCommand = new ActionCommand(() => { foreach (var node in Roots.SelectMany(n => n.Walk())) node.IsExpanded = true; });
        CollapseAllCommand = new ActionCommand(() => { foreach (var node in Roots.SelectMany(n => n.Walk())) node.IsExpanded = false; });
        DuplicateCommand = new ActionCommand(Duplicate, () => Selected is not null);
        DeleteCommand = new ActionCommand(Delete, () => Selected is not null);
        UpCommand = new ActionCommand(() => Move(-1), () => CanMove(-1));
        DownCommand = new ActionCommand(() => Move(1), () => CanMove(1));
        AddValueCommand = new ActionCommand(AddValue, () => Selected is not null);
        ClearValueCommand = new ActionCommand(() => { SelectedField?.ClearOverride(); BuildEditor(); }, () => SelectedField is not null);
        AddRecipeFieldCommand = new ActionCommand(AddRecipeField, () => CanEditRecipeTable);
        DeleteRecipeFieldCommand = new ActionCommand(DeleteRecipeField, () => SelectedRecipeField is not null && !SelectedRecipeField.IsLocked);
        RecipeFieldUpCommand = new ActionCommand(() => MoveRecipeField(-1), () => CanMoveRecipeField(-1));
        RecipeFieldDownCommand = new ActionCommand(() => MoveRecipeField(1), () => CanMoveRecipeField(1));
        AddAlarmCommand = new ActionCommand(AddAlarm, () => Selected is not null);
        DeleteAlarmCommand = new ActionCommand(() => { if (SelectedAlarm is not null) XmlLayout.Remove(SelectedAlarm.Element); BuildEditor(); }, () => SelectedAlarm is not null);
        InitializeChamberEditor();
    }

    private void OpenDialog()
    {
        var dialog = new OpenFileDialog { Title = "选择要配置的 machine.xml 文件", Filter = "machine.xml 配置 (*.xml)|*.xml|所有文件|*.*", FileName = "machine.xml" };
        if (dialog.ShowDialog() == true) OpenSafely(dialog.FileName);
    }

    private void OpenSafely(string path)
    {
        if (!ConfirmDiscard()) return;
        Try(() => { OpenFile(path); _ = LoadCatalogAsync(); });
    }

    public void OpenFile(string path)
    {
        var document = ConfigDocument.Open(path);
        ResetCdsLayouts(document.FilePath);
        ResetChamberLayouts(document.FilePath);
        if (_document is not null) _document.Changed -= DocumentChanged;
        _document = document;
        _document.Changed += DocumentChanged;
        Roots.Clear();
        foreach (var node in document.Xml.Root!.Elements("Setting")) Roots.Add(new(node));
        foreach (var group in Roots.Where(n => n.Type.Length == 0 && n.Children.Count > 0)) group.IsExpanded = true;
        Sources.Clear();
        _catalog = new(); Elements.Clear();
        if (_preferences.SourcesByFile.TryGetValue(document.FilePath, out var saved))
        {
            foreach (var source in saved.Where(source => File.Exists(source) || Directory.Exists(source)))
            {
                Sources.Add(source);
            }
        }
        if (Sources.Count == 0)
        {
            foreach (var source in ElementSourceDiscovery.Find(document.FilePath))
            {
                Sources.Add(source);
            }
        }
        Selected = Roots.FirstOrDefault();
        SelectNode(Selected);
        Status = "machine.xml 已打开；请检查元件来源，修改后重启后端生效。";
        Persist();
        DocumentChanged();
        Raise(nameof(CatalogSummary));
    }

    private void DocumentChanged()
    {
        Raise(nameof(Title)); Raise(nameof(FilePath)); Raise(nameof(ConfigDirectory));
        Raise(nameof(XmlPreview)); Raise(nameof(OriginalPreview)); Raise(nameof(SelectionPath));
        Raise(nameof(AddChildLabel));
        QueueChamberRefresh();
        QueueCdsRefresh();
    }

    public bool ConfirmDiscard()
    {
        if (_document?.IsDirty != true && !HasLayoutChanges)
        {
            return true;
        }
        var result = MessageBox.Show("当前 machine.xml 或布局有未保存修改。是否一起保存？", "ToolBuilder", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return result == MessageBoxResult.No || result == MessageBoxResult.Yes && Save(false);
    }

    private bool Save(bool saveAs)
    {
        if (_document is null) return false;
        Validate();
        if (Issues.Any(i => i.Severity == "错误"))
        {
            MessageBox.Show("配置存在错误，请先修正「校验结果」中的错误再保存。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        var renames = RenameImpact.Find(_document);
        if (renames.Count > 0 && MessageBox.Show(RenameImpact.Summary(renames), "改了元件名，确认保存",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return false;
        var target = _document.FilePath;
        if (saveAs)
        {
            var dialog = new SaveFileDialog { Title = "machine.xml 另存为", Filter = "machine.xml 配置 (*.xml)|*.xml", FileName = Path.GetFileName(target), InitialDirectory = Path.GetDirectoryName(target) };
            if (dialog.ShowDialog() != true) return false;
            target = dialog.FileName;
        }
        try
        {
            SaveWorkspace(target);
            Persist();
            Status = "machine.xml 和布局已保存；原文件备份在各自目录的 backup 中。machine.xml 装配配置需重启后端生效。";
            return true;
        }
        catch (Exception e) { ShowError(e); return false; }
    }

    private void AddDirectory()
    {
        var dialog = new OpenFolderDialog { Title = "选择元件目录（读取本层 DLL 及 Modules 子目录）", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        foreach (var path in dialog.FolderNames) if (!Sources.Contains(path, StringComparer.OrdinalIgnoreCase)) Sources.Add(path);
        _ = LoadCatalogAsync();
    }
    private void AddDll()
    {
        var dialog = new OpenFileDialog { Title = "选择元件 DLL（可多选，兄弟 DLL 用于解析依赖）", Filter = "程序集 (*.dll)|*.dll", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        foreach (var path in dialog.FileNames) if (!Sources.Contains(path, StringComparer.OrdinalIgnoreCase)) Sources.Add(path);
        _ = LoadCatalogAsync();
    }

    public async Task LoadCatalogAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Status = "正在读取元件元数据…";
        var snapshot = Sources.ToArray();
        try
        {
            _catalog = await Task.Run(() => ElementCatalog.Read(snapshot));
            Elements.Clear();
            foreach (var spec in _catalog.Elements.Values.OrderBy(c => c.TypeName)) Elements.Add(spec);
            foreach (var root in Roots) root.RefreshMetadata(_catalog);
            RefreshChamberVisualCatalog();
            RefreshChamberLayouts();
            RefreshCdsLayouts();
            BuildEditor();
            Raise(nameof(SelectionDescription)); Raise(nameof(CatalogSummary));
            Validate();
            Status = $"已加载 {Elements.Count} 个元件；{_catalog.Diagnostics.Count} 条加载提示。";
            Persist();
        }
        catch (Exception e) { ShowError(e); }
        finally { IsBusy = false; }
    }

    private void BuildEditor()
    {
        Fields.Clear(); Alarms.Clear(); RecipeFieldRows.Clear();
        SelectedField = null; SelectedAlarm = null; SelectedRecipeField = null;
        Raise(nameof(CanEditRecipeFields)); Raise(nameof(CanEditRecipeHead));
        Raise(nameof(SelectedType));
        if (Selected is null) { EditorSummaryChanged(); return; }
        var specs = _catalog.Elements.GetValueOrDefault(Selected.Type)?.Properties.ToList() ?? [];
        foreach (var value in Selected.Element.Elements("Value"))
        {
            var name = ConfigDocument.Attribute(value, "Name");
            if (!specs.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                bool isBoolean = bool.TryParse(ConfigDocument.Attribute(value, "Value"), out _);
                specs.Add(new(name, isBoolean ? "System.Boolean" : "System.String", "", "原有参数",
                    "XML 原有参数；未匹配 [Machine] 元数据，保留原值。", false, isBoolean ? ["true", "false"] : []));
            }
        }
        foreach (var spec in specs.OrderBy(s => s.Group)) Fields.Add(new(Selected.Element, spec));
        foreach (var alarm in Selected.Element.Elements("Alarm")) Alarms.Add(new(alarm));
        if (CanEditRecipeTable && _document is not null)
        {
            var types = RecipeFields.StationTypes(_document, _catalog);
            if (!types.SequenceEqual(RecipeOptionTypes)) { RecipeOptionTypes = types; Raise(nameof(RecipeOptionTypes)); }
            foreach (var field in Selected.Children) RecipeFieldRows.Add(new(field, CanEditRecipeHead, types));
        }
        EditorSummaryChanged();
    }
    private void EditorSummaryChanged()
    {
        Raise(nameof(HasFields)); Raise(nameof(HasAlarmOverrides));
        Raise(nameof(ParametersSummary));
        if (EditorPage == "Alarms" && !HasAlarmOverrides
            || EditorPage == "RecipeFields" && !CanEditRecipeFields || EditorPage == "RecipeHead" && !CanEditRecipeHead) EditorPage = "Parameters";
    }

    public void Validate()
    {
        Issues.Clear();
        if (_document is null)
        {
            return;
        }
        foreach (var issue in ConfigValidator.Validate(_document, _catalog))
        {
            Issues.Add(issue);
        }
        ValidateCdsLayouts();
        ValidateChamberLayouts();
        Status = $"校验完成：{Issues.Count(i => i.Severity == "错误")} 个错误，{Issues.Count(i => i.Severity == "警告")} 个提示。";
    }

    public void NavigateIssue(ValidationIssue issue)
    {
        var node = Roots.SelectMany(r => r.Walk()).FirstOrDefault(n => n.Path == issue.Path);
        if (node is not null) SelectNode(node);
    }
    public void SelectNode(NodeModel? node)
    {
        if (node is null) return;
        if (Selected is not null) Selected.IsSelected = false;
        for (var parent = node.Parent; parent is not null; parent = parent.Parent) parent.IsExpanded = true;
        node.IsSelected = true;
        Selected = node;
    }
    private ObservableCollection<NodeModel> Siblings(NodeModel node) => node.Parent?.Children ?? Roots;
    private XElement XmlParent(NodeModel? parent) => parent?.Element ?? _document!.Xml.Root!;
    private string UniqueName(string prefix, ObservableCollection<NodeModel> siblings)
    {
        var name = prefix;
        for (var suffix = 2; siblings.Any(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); suffix++) name = prefix + suffix;
        return name;
    }
    private void AddGroup(NodeModel? parent)
    {
        AddElement(parent, "分组");
    }
    private void AddElement(NodeModel? parent, string category = "元件")
    {
        var picker = new ElementPicker(Elements, parent?.Path ?? "machine.xml 根目录", category, this);
        picker.Owner = Application.Current.MainWindow;
        if (picker.ShowDialog() == true)
        {
            Try(() =>
            {
                CreateChild(parent, picker.ViewModel.IsGroup ? null : picker.ViewModel.SelectedElement);
                NameEditRequested?.Invoke();
            });
        }
    }
    public event Action? NameEditRequested;
    public NodeModel CreateChild(NodeModel? parent, ElementSpec? component)
    {
        var stem = component?.ShortName ?? "Group";
        foreach (var suffix in new[] { "Element", "Module" })
            if (stem.EndsWith(suffix, StringComparison.Ordinal)) { stem = stem[..^suffix.Length]; break; }
        if (string.IsNullOrWhiteSpace(stem)) stem = "Element";
        return CreateChild(parent, UniqueName(stem, parent?.Children ?? Roots), component);
    }
    public NodeModel CreateChild(NodeModel? parent, string name, ElementSpec? component)
    {
        if (_document is null) throw new InvalidOperationException("请先打开 machine.xml。");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("节点名称不能为空。");
        var siblings = parent?.Children ?? Roots;
        if (siblings.Any(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException($"同级节点 {name} 已存在，请换一个名称。");
        var element = new XElement("Setting", new XAttribute("Name", name));
        if (component is not null)
        {
            element.SetAttributeValue("Type", component.Key);
            foreach (var field in component.Properties) ConfigDocument.SetValue(element, field.Name, field.DefaultValue);
        }
        XmlLayout.Add(XmlParent(parent), element);
        var model = new NodeModel(element, parent);
        model.RefreshMetadata(_catalog);
        siblings.Add(model);
        if (parent is not null) parent.IsExpanded = true;
        SelectNode(model);
        Status = $"已在 {parent?.Path ?? "machine.xml 根目录"} 下添加 {name}，可直接编辑其配置。";
        return model;
    }
    private void Duplicate()
    {
        if (Selected is null) return;
        var current = Selected;
        var siblings = Siblings(current);
        var copy = new XElement(current.Element);
        copy.SetAttributeValue("Name", UniqueName(current.Name + "Copy", siblings));
        XmlLayout.AddAfter(current.Element, copy);
        var model = new NodeModel(copy, current.Parent);
        model.RefreshMetadata(_catalog);
        siblings.Insert(siblings.IndexOf(current) + 1, model);
        SelectNode(model);
        Status = "已复制子树；请核对名称、IO 索引、PLC 地址及站点引用。";
    }
    private void Delete()
    {
        if (Selected is null) return;
        if (MessageBox.Show($"删除 {Selected.Path} 及全部子节点？", "删除节点", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var parent = Selected.Parent;
        RemoveNode(Selected);
        SelectNode(parent ?? Roots.FirstOrDefault());
    }
    private void RemoveNode(NodeModel node)
    {
        XmlLayout.Remove(node.Element); Siblings(node).Remove(node);
        if (ReferenceEquals(Selected, node)) Selected = null;
    }
    private bool CanMove(int offset) => Selected is not null && Siblings(Selected).IndexOf(Selected) + offset >= 0
        && Siblings(Selected).IndexOf(Selected) + offset < Siblings(Selected).Count;
    private void Move(int offset)
    {
        if (!CanMove(offset) || Selected is null) return;
        var siblings = Siblings(Selected);
        var old = siblings.IndexOf(Selected);
        var other = siblings[old + offset];
        XmlLayout.Remove(Selected.Element);
        if (offset < 0) XmlLayout.AddBefore(other.Element, Selected.Element); else XmlLayout.AddAfter(other.Element, Selected.Element);
        siblings.Move(old, old + offset);
    }
    private void AddValue()
    {
        if (Selected is null) return;
        var name = TextPrompt.Ask("添加参数", "参数名称", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Fields.Any(f => f.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))) { Status = "参数已存在，请在表格中修改。"; return; }
        ConfigDocument.SetValue(Selected.Element, name.Trim(), ""); BuildEditor();
    }
    /// <summary>添加配方字段：问字段名，默认中文名同字段名、类型小数；插在选中那一行后面（没选中就加在最后）。</summary>
    private void AddRecipeField()
    {
        if (!CanEditRecipeTable || Selected is null) return;
        var container = Selected;
        var key = TextPrompt.Ask("添加配方字段", "字段名（英文，字母开头；会写进配方文件，以后别随便改）", "");
        if (string.IsNullOrWhiteSpace(key)) return;
        var element = new XElement("Setting", new XAttribute("Name", UniqueName(key.Trim(), container.Children)));
        ConfigDocument.SetValue(element, "Text", key.Trim());
        ConfigDocument.SetValue(element, "Type", "Double");
        var model = new NodeModel(element, container);
        model.RefreshMetadata(_catalog);
        var anchor = SelectedRecipeField?.Node;
        if (anchor is not null)
        {
            XmlLayout.AddAfter(anchor.Element, element);
            container.Children.Insert(container.Children.IndexOf(anchor) + 1, model);
        }
        else
        {
            XmlLayout.Add(container.Element, element);
            container.Children.Add(model);
        }
        SelectRecipeField(model);
        Status = CanEditRecipeHead
            ? $"已添加配方头的一项 {model.Name}：在表格里填中文名、类型和范围；工艺配方页的配方头多一项，在页面上填。"
            : $"已添加配方字段 {model.Name}：在表格里填中文名、类型和范围；工艺配方页上多一列。";
    }

    /// <summary>删除配方字段（时间那一行删不了）：已有配方里这一项的值，下次保存时丢掉。</summary>
    private void DeleteRecipeField()
    {
        var row = SelectedRecipeField;
        if (row is null || row.IsLocked) return;
        RemoveNode(row.Node);
        BuildEditor();
        Status = $"已删除配方字段 {row.Key}；已有配方里这一项的值，下次在配方页保存时丢掉。";
    }

    private bool CanMoveRecipeField(int offset)
    {
        var row = SelectedRecipeField;
        if (row?.Node.Parent is null) return false;
        var index = row.Node.Parent.Children.IndexOf(row.Node) + offset;
        return index >= 0 && index < row.Node.Parent.Children.Count;
    }

    /// <summary>上移、下移字段：配方页上的列跟着换顺序。</summary>
    private void MoveRecipeField(int offset)
    {
        var row = SelectedRecipeField;
        if (row?.Node.Parent is null || !CanMoveRecipeField(offset)) return;
        var siblings = row.Node.Parent.Children;
        var old = siblings.IndexOf(row.Node);
        var other = siblings[old + offset];
        XmlLayout.Remove(row.Node.Element);
        if (offset < 0) XmlLayout.AddBefore(other.Element, row.Node.Element); else XmlLayout.AddAfter(other.Element, row.Node.Element);
        siblings.Move(old, old + offset);
        SelectRecipeField(row.Node);
    }

    private void SelectRecipeField(NodeModel node)
    {
        BuildEditor();
        SelectedRecipeField = RecipeFieldRows.FirstOrDefault(r => ReferenceEquals(r.Node, node));
        Raise(nameof(SelectedRecipeField));
    }

    /// <summary>打开选项窗口：一行一个地编辑这一格的选项（步骤的是某种 Type 的那一套），确定后写回这一格。</summary>
    public void EditRecipeOptions(RecipeOptionCell cell)
    {
        if (!cell.Row.HasOptions) return;
        var model = new OptionsDialogViewModel(cell.Row.Key, cell.Row.Text, cell.Row.IsHead ? null : cell.StationType, cell.Text);
        var dialog = new OptionsDialog(model) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true) cell.Text = dialog.ViewModel.Result;
    }

    private void AddAlarm()
    {
        if (Selected is null) return;
        var name = TextPrompt.Ask("添加报警覆盖", "报警名称（对应代码声明）", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        XmlLayout.Add(Selected.Element, new XElement("Alarm", new XAttribute("Name", name.Trim()), new XAttribute("Level", "Alarm1")));
        BuildEditor();
    }
    private void Persist()
    {
        if (_document is null || !_rememberSettings) return;
        _preferences.SourcesByFile[_document.FilePath] = Sources.ToList();
        _preferences.RecentFiles.RemoveAll(f => f.Equals(_document.FilePath, StringComparison.OrdinalIgnoreCase));
        _preferences.RecentFiles.Insert(0, _document.FilePath);
        _preferences.RecentFiles = _preferences.RecentFiles.Take(12).ToList();
        RecentFiles.Clear(); foreach (var path in _preferences.RecentFiles) RecentFiles.Add(path);
        try { _preferences.Save(); } catch (Exception e) { Status = $"配置可继续编辑，最近记录保存失败：{e.Message}"; }
    }
    private void Try(Action action) { try { action(); } catch (Exception e) { ShowError(e); } }
    private void ShowError(Exception exception)
    {
        Status = exception.Message;
        MessageBox.Show(exception.Message, "ToolBuilder", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
