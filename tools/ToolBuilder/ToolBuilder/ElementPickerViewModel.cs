using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using ToolBuilder.Core;

namespace ToolBuilder;

public sealed class ElementPickerViewModel : Observable
{
    public ObservableCollection<ElementSpec> Elements { get; }
    public ICollectionView FilteredElements { get; }
    public string ParentPath { get; }
    public IReadOnlyList<string> Categories { get; } = ["元件", "分组"];
    private string _search = "";
    public string Search { get => _search; set { if (Set(ref _search, value)) Refresh(); } }
    private string _category = "元件";
    public string Category
    {
        get => _category;
        set
        {
            if (!Categories.Contains(value) || !Set(ref _category, value)) return;
            if (IsGroup)
            {
                SelectedElement = null;
            }
            Raise(nameof(IsElement)); Raise(nameof(IsGroup)); Raise(nameof(SelectedProperties));
            Raise(nameof(SelectedDescription)); Raise(nameof(SelectedType)); Raise(nameof(SelectedAssembly));
            Raise(nameof(ConfirmLabel));
            Refresh(); CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool IsGroup => Category == "分组";
    public bool IsElement => !IsGroup;
    public string ConfirmLabel => IsGroup ? "添加分组" : "添加并配置";
    private ElementSpec? _selected;
    public ElementSpec? SelectedElement
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            Raise(nameof(SelectedProperties)); Raise(nameof(SelectedDescription)); Raise(nameof(SelectedType));
            Raise(nameof(SelectedAssembly)); Raise(nameof(HasSelection));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool HasSelection => SelectedElement is not null;
    public IEnumerable<PropertySpec> SelectedProperties => IsElement ? SelectedElement?.Properties ?? [] : [];
    public string SelectedDescription => IsGroup ? "分组用于组织下级节点，不设置 Type。添加后可在主界面修改名称，继续添加元件或分组。"
        : SelectedElement?.Description ?? "选择左侧元件，查看用途和配置项。";
    public string SelectedType => SelectedElement?.Key ?? "";
    public string SelectedAssembly => SelectedElement?.AssemblyPath ?? "";
    public bool IsEmpty => IsElement && !FilteredElements.Cast<object>().Any();
    public string EmptyMessage => Elements.Count == 0
        ? "尚未识别到元件。请先编译元件项目，或点击「元件来源…」选择元件 DLL / 编译输出目录。"
        : "没有匹配的元件，请修改搜索内容。";
    public string Summary => IsGroup ? "分组无需选择 DLL 类型。" : Elements.Count == 0 ? "未加载元件，请选择元件来源。"
        : $"{FilteredElements.Cast<object>().Count()} / {Elements.Count} 个可选元件";
    public ICommand ConfirmCommand { get; }
    public event Action? Confirmed;

    public ElementPickerViewModel(IEnumerable<ElementSpec> components, string parentPath)
    {
        ParentPath = parentPath;
        Elements = new(components.OrderBy(c => c.ShortName));
        FilteredElements = CollectionViewSource.GetDefaultView(Elements);
        FilteredElements.Filter = item => IsElement && item is ElementSpec spec
            && $"{spec.Key} {spec.ShortName} {spec.Description}".Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase);
        ConfirmCommand = new ActionCommand(() => Confirmed?.Invoke(), () => IsGroup || SelectedElement is not null);
    }
    private void Refresh()
    {
        FilteredElements.Refresh();
        if (SelectedElement is not null && !FilteredElements.Contains(SelectedElement)) SelectedElement = null;
        Raise(nameof(Summary));
        Raise(nameof(IsEmpty));
        Raise(nameof(EmptyMessage));
    }

    public void ReplaceElements(IEnumerable<ElementSpec> components)
    {
        string? selectedKey = SelectedElement?.Key;
        Elements.Clear();
        foreach (var component in components.OrderBy(component => component.ShortName))
        {
            Elements.Add(component);
        }
        SelectedElement = Elements.FirstOrDefault(component => component.Key == selectedKey);
        Refresh();
    }
}
