using ToolBuilder.Core;

namespace ToolBuilder;

/// <summary>
/// "配方字段"、"配方头"页的一行：工艺配方字段表里的一个字段节点（节点名就是字段名），各列直接读写节点下的 Value。
/// 空的不写进 machine.xml；换类型时把这个类型用不上的参数清掉（下拉没有单位、上下限，不是下拉的没有选项）。
/// 步骤的下拉按腔体的每种 Type 一格选项（Source.类型），配方头的下拉只有一格（Source）。
/// 锁着的行：步骤的时间（Seconds），配方头固定的几项——字段名、类型、必填不能改，也不能删；配方头程序算的那几项没有范围、默认值。
/// </summary>
public sealed class RecipeFieldModel : Observable
{
    private readonly NodeModel _node;
    private readonly IReadOnlyList<string> _stationTypes;

    public RecipeFieldModel(NodeModel node, bool isHead, IReadOnlyList<string> stationTypes)
    {
        _node = node;
        _stationTypes = stationTypes;
        IsHead = isHead;
        IReadOnlyList<string> cellTypes = isHead ? [""] : stationTypes;
        OptionCells = cellTypes.Select(type => new RecipeOptionCell(this, type)).ToList();
    }

    public NodeModel Node => _node;

    /// <summary>配方头的一项（不是步骤的一列）。</summary>
    public bool IsHead { get; }

    /// <summary>下拉的选项格：步骤的一种 Type 一格，配方头只有一格。</summary>
    public IReadOnlyList<RecipeOptionCell> OptionCells { get; }

    public string Key
    {
        get => _node.Name;
        set { if (IsLocked) return; _node.Name = (value ?? "").Trim(); Raise(); RaiseShape(); }
    }

    public string Text { get => Get(nameof(Text)); set { Put(nameof(Text), value); _node.RefreshPaths(); } }
    public string TextEn { get => Get(nameof(TextEn)); set => Put(nameof(TextEn), value); }

    public string Type
    {
        get => Get(nameof(Type));
        set
        {
            if (IsLocked || value is null || value == Type) return;
            Put(nameof(Type), value);
            // 这个类型用不上的参数清掉，免得 machine.xml 里留着后端不认的东西
            if (!RecipeFields.IsNumber(value)) { Put(nameof(Unit), ""); Put(nameof(Min), ""); Put(nameof(Max), ""); }
            if (!value.Equals("Double", StringComparison.OrdinalIgnoreCase)) Put(nameof(Decimals), "");
            if (!value.Equals("Choice", StringComparison.OrdinalIgnoreCase))
                foreach (var source in _node.Element.Elements("Value").Where(v => RecipeFields.IsSourceKey(ConfigDocument.Attribute(v, "Name"))).ToList())
                    XmlLayout.Remove(source);
            Put(nameof(Default), "");
            RaiseShape();
        }
    }

    public string Unit { get => Get(nameof(Unit)); set => Put(nameof(Unit), value); }
    public string Min { get => Get(nameof(Min)); set => Put(nameof(Min), value); }
    public string Max { get => Get(nameof(Max)); set => Put(nameof(Max), value); }
    public string Decimals { get => Get(nameof(Decimals)); set => Put(nameof(Decimals), value); }
    public string Default
    {
        get => Get(nameof(Default));
        set
        {
            Put(nameof(Default), value);
            Raise(nameof(BooleanDefault));
        }
    }
    public bool IsBoolean => Type.Equals("Bool", StringComparison.OrdinalIgnoreCase);
    public string? BooleanDefault
    {
        get => bool.TryParse(Default, out bool flag) ? flag ? "true" : "false" : null;
        set
        {
            if (CanDefault && value is not null && bool.TryParse(value, out bool flag))
            {
                Default = flag ? "true" : "false";
            }
        }
    }
    public System.Windows.Input.ICommand ClearDefaultCommand => new ActionCommand(() => Default = "", () => CanDefault);

    /// <summary>必填：true 写入配置，false 使用默认的不必填。</summary>
    public bool Required
    {
        get => Get(nameof(Required)).Equals("true", StringComparison.OrdinalIgnoreCase);
        set { if (IsLocked) return; Put(nameof(Required), value ? "true" : ""); }
    }

    /// <summary>锁着的行：步骤的时间（Seconds），配方头固定的几项。</summary>
    public bool IsLocked => IsHead ? RecipeFields.IsHeadFixed(_node.Name) : _node.Name.Equals(RecipeFields.SecondsKey, StringComparison.OrdinalIgnoreCase);
    public bool IsEditable => !IsLocked;

    /// <summary>配方头里程序算的那几项：没有范围、默认值、选项。</summary>
    public bool IsAuto => IsHead && RecipeFields.IsHeadAuto(_node.Name);

    /// <summary>配方头的 Type：选项是腔体配的 Type，不用写。</summary>
    public bool IsHeadType => IsHead && _node.Name.Equals(RecipeFields.TypeKey, StringComparison.OrdinalIgnoreCase);

    public bool IsNumber => !IsAuto && RecipeFields.IsNumber(Type);
    public bool IsDouble => !IsAuto && Type.Equals("Double", StringComparison.OrdinalIgnoreCase);
    public bool IsChoice => Type.Equals("Choice", StringComparison.OrdinalIgnoreCase);

    /// <summary>要写选项的下拉（配方头的 Type 不用写）。</summary>
    public bool HasOptions => IsChoice && !IsHeadType;

    /// <summary>能填默认值：程序算的、Type 不填。</summary>
    public bool CanDefault => !IsAuto && !IsHeadType;

    /// <summary>"值从哪来"那一栏：写选项的下拉显示选项格，别的显示一句话。</summary>
    public bool ShowsSourceText => !HasOptions;

    /// <summary>"值从哪来"那一栏的那句话：程序算的写怎么来的，Type 写腔体配的几种，别的是在配方页上填。</summary>
    public string SourceText => IsAuto ? RecipeFields.HeadAutoText(_node.Name)
        : IsHeadType ? $"选：machine.xml 里腔体配的 Type（{string.Join("、", _stationTypes.Select(type => type.Length == 0 ? "没配" : type))}）"
        : "填";

    internal string Get(string name) => ConfigDocument.Value(_node.Element, name) ?? "";

    internal void Put(string name, string? value)
    {
        value = (value ?? "").Trim();
        if (value.Length == 0)
        {
            var existing = _node.Element.Elements("Value").FirstOrDefault(v => ConfigDocument.Attribute(v, "Name").Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) XmlLayout.Remove(existing);
        }
        else ConfigDocument.SetValue(_node.Element, name, value);
        Raise(name);
    }

    /// <summary>字段名、类型变了：哪些格能填跟着变。</summary>
    private void RaiseShape()
    {
        Raise(nameof(IsLocked)); Raise(nameof(IsEditable)); Raise(nameof(IsAuto)); Raise(nameof(IsHeadType));
        Raise(nameof(IsNumber)); Raise(nameof(IsDouble)); Raise(nameof(IsChoice)); Raise(nameof(HasOptions));
        Raise(nameof(CanDefault)); Raise(nameof(ShowsSourceText)); Raise(nameof(SourceText));
        Raise(nameof(IsBoolean)); Raise(nameof(BooleanDefault));
        foreach (var cell in OptionCells) cell.Refresh();
    }
}

/// <summary>一行里的一格选项：步骤的是一种 Type 的那一套（Source.类型，没配 Type 的是 Source），配方头的是 Source。</summary>
public sealed class RecipeOptionCell(RecipeFieldModel row, string stationType) : Observable
{
    public RecipeFieldModel Row => row;
    public string StationType => stationType;

    /// <summary>写在 machine.xml 的哪个参数。</summary>
    public string Key => RecipeFields.SourceKey(stationType);

    /// <summary>这一套选项，英文逗号隔开。</summary>
    public string Text { get => row.Get(Key); set { row.Put(Key, value); Raise(); } }

    public void Refresh() => Raise(nameof(Text));
}
