using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using ToolBuilder.Core;

namespace ToolBuilder;

/// <summary>配方字段下拉的选项：一行一个地加、删、排先后，确定后连成 machine.xml 里的写法（英文逗号隔开）。</summary>
public partial class OptionsDialog : Window
{
    public OptionsDialogViewModel ViewModel { get; }

    public OptionsDialog(OptionsDialogViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void Ok(object sender, RoutedEventArgs e) => DialogResult = true;
}

/// <summary>选项窗口的内容：格子里原来写的按英文逗号拆成一行一个，改完再连回去（空行丢掉）。</summary>
public sealed class OptionsDialogViewModel : Observable
{
    /// <summary>stationType：步骤的下拉是哪种腔体 Type 的那一套（没配 Type 的是空字符串）；配方头的下拉传 null。</summary>
    public OptionsDialogViewModel(string key, string text, string? stationType, string source)
    {
        var typeText = stationType is null ? "" : $" · Type {(stationType.Length == 0 ? "（没配）" : stationType)}";
        Title = $"{(text.Length > 0 ? text : key)}（{key}）{typeText} 的选项";
        foreach (var option in RecipeFields.SplitOptions(source)) Items.Add(new OptionItem(option));
        AddCommand = new ActionCommand(Add);
        DeleteCommand = new ActionCommand(() => { if (Selected is not null) Items.Remove(Selected); }, () => Selected is not null);
        UpCommand = new ActionCommand(() => Move(-1), () => Selected is not null && Items.IndexOf(Selected) > 0);
        DownCommand = new ActionCommand(() => Move(1), () => Selected is not null && Items.IndexOf(Selected) < Items.Count - 1);
    }

    public string Title { get; }
    public ObservableCollection<OptionItem> Items { get; } = [];

    private OptionItem? _selected;
    public OptionItem? Selected { get => _selected; set => Set(ref _selected, value); }

    public ICommand AddCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand DownCommand { get; }

    /// <summary>写回格子里的样子。</summary>
    public string Result => RecipeFields.JoinOptions(Items.Select(item => item.Text));

    private void Add()
    {
        var item = new OptionItem("");
        Items.Add(item);
        Selected = item;
    }

    private void Move(int offset)
    {
        if (Selected is null) return;
        var index = Items.IndexOf(Selected);
        Items.Move(index, index + offset);
    }
}

/// <summary>选项窗口里的一行。</summary>
public sealed class OptionItem(string text) : Observable
{
    private string _text = text;
    public string Text { get => _text; set => Set(ref _text, value ?? ""); }
}
