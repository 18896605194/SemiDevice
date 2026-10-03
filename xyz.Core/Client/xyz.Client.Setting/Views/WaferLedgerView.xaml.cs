using System.Windows.Controls;
using System.Windows.Threading;
using xyz.Client.Setting.ViewModels;
using xyz.Tools;

namespace xyz.Client.Setting.Views;

public partial class WaferLedgerView : UserControl
{
    public WaferLedgerView()
    {
        InitializeComponent();
        var viewModel = IocHelper.GetRequiredService<WaferLedgerViewModel>();
        DataContext = viewModel;

        // 页面启动时就挂上、切菜单只切可见性：不在前台时账变了不拉，切回来再拉
        IsVisibleChanged += (_, args) => viewModel.SetPageVisible((bool)args.NewValue);

        // 新建账单的确认框一弹出来，光标就落在片号框里，直接输
        NewWaferIdBox.IsVisibleChanged += (_, args) =>
        {
            if ((bool)args.NewValue)
            {
                Dispatcher.BeginInvoke(() => NewWaferIdBox.Focus(), DispatcherPriority.Input);
            }
        };
    }

    /// <summary>
    /// 槽位表选中的行（点的、重拉后找回来的）滚到看得见的地方。
    /// </summary>
    private void OnSlotSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        var grid = (DataGrid)sender;
        var selected = grid.SelectedItem;
        if (selected is not null)
        {
            grid.ScrollIntoView(selected);
        }
    }
}
