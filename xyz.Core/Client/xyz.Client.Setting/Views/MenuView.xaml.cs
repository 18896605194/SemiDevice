using System.Windows;
using System.Windows.Controls;
using xyz.Client.DataModels.Ioc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Setting.Models;
using xyz.Client.Setting.ViewModels;

namespace xyz.Client.Setting.Views;

public partial class MenuView : UserControl
{
    public MenuView()
    {
        InitializeComponent();
        DataContext = IocHelper.GetRequiredService<MenuViewModel>();
    }

    private void OnTreeViewSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MenuViewModel viewModel && e.NewValue is MenuDisplayModel menu)
        {
            viewModel.SelectedMenu = menu;
        }
    }
}
