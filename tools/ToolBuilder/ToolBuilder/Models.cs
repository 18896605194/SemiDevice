using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Xml.Linq;
using ToolBuilder.Core;

namespace ToolBuilder;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public sealed class ActionCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
}

public sealed class NodeModel : Observable
{
    public XElement Element { get; }
    public NodeModel? Parent { get; }
    public ObservableCollection<NodeModel> Children { get; } = [];
    public NodeModel(XElement element, NodeModel? parent = null)
    {
        Element = element;
        Parent = parent;
        foreach (var child in element.Elements("Setting")) Children.Add(new(child, this));
        Children.CollectionChanged += (_, _) => { Raise(nameof(ChildCount)); Raise(nameof(ChildSummary)); Raise(nameof(Description)); };
    }
    private ElementCatalog? _catalog;
    public void RefreshMetadata(ElementCatalog catalog)
    {
        _catalog = catalog;
        Raise(nameof(Kind)); Raise(nameof(Description)); Raise(nameof(Category)); Raise(nameof(BadgeColor));
        foreach (var child in Children) child.RefreshMetadata(catalog);
    }
    private ElementSpec? Spec => _catalog?.Elements.GetValueOrDefault(Type);
    public string Category => string.IsNullOrWhiteSpace(Type) ? "分组" : "元件";
    private bool IsRecipeField => Parent is not null && RecipeFields.IsFieldsNode(Parent.Element);
    private bool IsRecipeHeadField => Parent is not null && RecipeFields.IsHeadNode(Parent.Element);
    public string BadgeColor => Category == "分组" ? "#66768B" : "#167A74";
    public string Description => Spec?.Description is { Length: > 0 } text ? text
        : IsRecipeField ? "配方字段：" + (ConfigDocument.Value(Element, "Text") ?? "")
        : IsRecipeHeadField ? "配方头：" + (ConfigDocument.Value(Element, "Text") ?? "")
        : Type.Length == 0 ? Children.Count > 0 ? $"{Children.Count} 个下级节点" : "配置分组" : "类型尚未加载";
    public int ChildCount => Children.Count;
    public string ChildSummary => $"{Children.Count} 个子节点";
    public string Name
    {
        get => ConfigDocument.Attribute(Element, "Name");
        set { ConfigDocument.SetAttribute(Element, "Name", value); RefreshPaths(); }
    }
    public string Type
    {
        get => ConfigDocument.Attribute(Element, "Type");
        set { ConfigDocument.SetAttribute(Element, "Type", value); Raise(); Raise(nameof(Kind)); Raise(nameof(Description)); Raise(nameof(Category)); Raise(nameof(BadgeColor)); }
    }
    public string InitOrder { get => ConfigDocument.Attribute(Element, "InitOrder"); set => ConfigDocument.SetAttribute(Element, "InitOrder", value); }
    public string Role { get => ConfigDocument.Attribute(Element, "Role"); set => ConfigDocument.SetAttribute(Element, "Role", value); }
    public string Path => ConfigDocument.PathOf(Element);
    public string Kind => string.IsNullOrWhiteSpace(Type) ? "分组" : Type.Split('.').Last();
    private bool _selected;
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
    private bool _expanded;
    public bool IsExpanded { get => _expanded; set => Set(ref _expanded, value); }
    public void RefreshPaths()
    {
        Raise(nameof(Name)); Raise(nameof(Path)); Raise(nameof(Category)); Raise(nameof(BadgeColor)); Raise(nameof(Description));
        foreach (var child in Children) child.RefreshPaths();
    }
    public IEnumerable<NodeModel> Walk() { yield return this; foreach (var child in Children) foreach (var node in child.Walk()) yield return node; }
}

public sealed class FieldModel : Observable
{
    public XElement Node { get; }
    public PropertySpec Spec { get; }
    public string Name => Spec.Name;
    public string Group => Spec.Group.Length == 0 ? "其他" : Spec.Group;
    public string Description => Spec.Description;
    public string DefaultValue => Spec.NormalizeValue(Spec.DefaultValue);
    public string TypeLabel => Spec.Choices.Count > 0 ? "选择" : Spec.Kind.Replace("System.", "");
    public string Required => Spec.Required ? "必填" : "";
    public IReadOnlyList<string> Choices => Spec.Choices;
    public bool HasChoices => Choices.Count > 0;
    public bool IsBoolean => Spec.Kind == "System.Boolean";
    public bool HasEditableChoices => HasChoices && !IsBoolean;
    public string? BooleanValue
    {
        get => bool.TryParse(Value, out bool flag) ? flag ? "true" : "false" : null;
        set
        {
            if (value is not null && bool.TryParse(value, out bool flag))
            {
                Value = flag ? "true" : "false";
                Raise();
            }
        }
    }
    public string Origin => ConfigDocument.Value(Node, Name) is null ? "默认" : "已配置";
    public string Value
    {
        get => Spec.NormalizeValue(ConfigDocument.Value(Node, Name) ?? DefaultValue);
        set
        {
            ConfigDocument.SetValue(Node, Name, Spec.NormalizeValue(value ?? ""));
            Raise();
            Raise(nameof(BooleanValue));
            Raise(nameof(Origin));
            Raise(nameof(Error));
        }
    }
    public string? Error => ConfigValidator.CheckValue(Spec, Value);
    public FieldModel(XElement node, PropertySpec spec) { Node = node; Spec = spec; }
    public void ClearOverride()
    {
        if (Node.Elements("Value").FirstOrDefault(x => ConfigDocument.Attribute(x, "Name").Equals(Name, StringComparison.OrdinalIgnoreCase)) is { } value)
            XmlLayout.Remove(value);
        Raise(nameof(Value)); Raise(nameof(Origin)); Raise(nameof(Error));
        Raise(nameof(BooleanValue));
    }
}

public sealed class AlarmModel(XElement node)
{
    public XElement Element => node;
    public string Name { get => ConfigDocument.Attribute(node, "Name"); set => ConfigDocument.SetAttribute(node, "Name", value); }
    public string Level { get => ConfigDocument.Attribute(node, "Level"); set => ConfigDocument.SetAttribute(node, "Level", value); }
}
