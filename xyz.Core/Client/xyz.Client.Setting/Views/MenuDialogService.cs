using System.Windows;
using xyz.Shared.Dtos;

namespace xyz.Client.Setting.Views;

/// <summary>
/// 菜单弹窗服务。
/// </summary>
public static class MenuDialogService
{
    public static MenuEditResult? Show(IEnumerable<MenuDto> parentMenus, MenuDto? initial = null)
    {
        var dialog = new MenuEditDialog(parentMenus, initial)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true ? dialog.Result : null;
    }
}
