using System.Windows;
using System.Windows.Controls;

namespace ToolBuilder;

public static class TextPrompt
{
    public static string? Ask(string title, string caption, string initial)
    {
        var window = new Window { Title = title, Width = 510, Height = 195, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var input = new TextBox { Text = initial };
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var okay = new Button { Content = "确定", IsDefault = true };
        okay.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(okay);
        buttons.Children.Add(new Button { Content = "取消", IsCancel = true });
        panel.Children.Add(buttons); window.Content = panel;
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? input.Text : null;
    }
}
