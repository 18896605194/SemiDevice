using System.Windows.Controls;
using System.Windows.Threading;
using xyz.Client.Recipe.ViewModels;
using xyz.Tools;

namespace xyz.Client.Recipe.Views;

public partial class SequenceView : UserControl
{
    public SequenceView()
    {
        InitializeComponent();
        var viewModel = IocHelper.GetRequiredService<SequenceViewModel>();
        DataContext = viewModel;

        // 页面启动时就挂上、切菜单只切可见性：不在前台时有变化不拉，切回来再拉
        IsVisibleChanged += (_, args) => viewModel.SetPageVisible((bool)args.NewValue);

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
