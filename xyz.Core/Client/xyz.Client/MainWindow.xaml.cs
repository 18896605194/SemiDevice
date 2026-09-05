using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using xyz.Client.Setting.Models;
using xyz.Client.ViewModels;

namespace xyz.Client;

/// <summary>
/// 主窗口交互逻辑。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        UpdateMaximizeRestoreIcon();

        DataContext = App.Services.GetRequiredService<MainViewModel>();
    }

    private void OnPrimaryMenuPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: MenuModel menu }
            && DataContext is MainViewModel viewModel)
        {
            viewModel.OpenPrimaryMenu(menu);
        }
    }

    private void OnSecondaryMenuPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox { SelectedItem: MenuModel menu }
            && DataContext is MainViewModel viewModel)
        {
            viewModel.SelectSecondaryMenu(menu);
        }
    }

    private void OnSecondaryMenuOverlayMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.IsSecondaryMenuOpen = false;
        }
    }

    private void OnSecondaryMenuCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnWindowStateChanged(object sender, EventArgs e)
    {
        UpdateMaximizeRestoreIcon();
    }

    private void UpdateMaximizeRestoreIcon()
    {
        MaximizeRestoreIcon.Kind = WindowState == WindowState.Maximized
            ? PackIconKind.WindowRestore
            : PackIconKind.WindowMaximize;
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            CenterOnScreen();
        }
        else
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void CenterOnScreen()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }
}
