using System.Windows;
using xyz.Shared.Dtos;

namespace xyz.Client.Setting.Views;

/// <summary>
/// 菜单创建/编辑弹窗。
/// </summary>
public partial class MenuEditDialog : Window
{
    private readonly List<ParentMenuOption> _parentOptions;

    public MenuEditDialog(IEnumerable<MenuDto>? parentMenus, MenuDto? initial = null)
    {
        InitializeComponent();

        _parentOptions = new List<ParentMenuOption>
        {
            new(null, "无")
        };

        foreach (var menu in parentMenus ?? [])
        {
            _parentOptions.Add(new ParentMenuOption(menu.Id, menu.Name));
        }

        ParentComboBox.ItemsSource = _parentOptions;
        ParentComboBox.DisplayMemberPath = nameof(ParentMenuOption.Name);

        if (initial != null)
        {
            NameTextBox.Text = initial.Name;
            CodeTextBox.Text = initial.Code;
            SortTextBox.Text = initial.Sort.ToString();
            IsEnabledCheckBox.IsChecked = initial.IsEnabled;
            ParentComboBox.SelectedItem = _parentOptions.FirstOrDefault(
                option => option.Id == initial.ParentId) ?? _parentOptions[0];
        }
        else
        {
            ParentComboBox.SelectedItem = _parentOptions[0];
        }
    }

    public MenuEditResult Result
    {
        get
        {
            var selectedParent = (ParentMenuOption?)ParentComboBox.SelectedItem;
            return new MenuEditResult(
                NameTextBox.Text.Trim(),
                CodeTextBox.Text.Trim(),
                selectedParent?.Id,
                int.TryParse(SortTextBox.Text, out var sort) ? sort : 1,
                IsEnabledCheckBox.IsChecked == true);
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameTextBox.Text) ||
            string.IsNullOrWhiteSpace(CodeTextBox.Text))
        {
            MessageBox.Show("请填写菜单名称和菜单编码", "菜单", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

public sealed record MenuEditResult(
    string Name,
    string Code,
    long? ParentId,
    int Sort,
    bool IsEnabled);

public sealed record ParentMenuOption(long? Id, string Name);
