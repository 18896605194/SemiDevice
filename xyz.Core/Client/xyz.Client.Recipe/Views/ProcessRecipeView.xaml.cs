using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using xyz.Client.Recipe.ViewModels;
using xyz.Tools;

namespace xyz.Client.Recipe.Views;

public partial class ProcessRecipeView : UserControl
{
    public ProcessRecipeView()
    {
        InitializeComponent();
        var viewModel = IocHelper.GetRequiredService<ProcessRecipeViewModel>();
        DataContext = viewModel;

        // 页面启动时就挂上、切菜单只切可见性：不在前台时有变化不拉，切回来再拉
        IsVisibleChanged += (_, args) => viewModel.SetPageVisible((bool)args.NewValue);

        // 点进某一行的输入框、下拉框时把那一行选中（输入框、下拉框自己吃掉了鼠标，行收不到点击）：添加、删除照着它来
        StepList.PreviewGotKeyboardFocus += (_, args) =>
        {
            if (args.NewFocus is DependencyObject focused && ItemsControl.ContainerFromElement(StepList, focused) is ListBoxItem item)
            {
                item.IsSelected = true;
            }
        };

        // 新建、重命名的确认框一弹出来，光标就落在名称框里，直接输
        NameBox.IsVisibleChanged += (_, args) =>
        {
            if ((bool)args.NewValue)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    NameBox.Focus();
                    NameBox.SelectAll();
                }, DispatcherPriority.Input);
            }
        };
    }
}
