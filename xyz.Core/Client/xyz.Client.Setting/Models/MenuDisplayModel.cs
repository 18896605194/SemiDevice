using System.Collections.ObjectModel;

namespace xyz.Client.Setting.Models;

/// <summary>
/// 菜单管理页面展示模型（树形态）。
/// </summary>
public class MenuDisplayModel
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public long? ParentId { get; set; }

    public string ParentName { get; set; } = string.Empty;

    public int Sort { get; set; }

    public bool IsEnabled { get; set; } = true;

    public ObservableCollection<MenuDisplayModel> Children { get; } = new();
}
