using System.Windows;
using xyz.Client.DataModels.Events;
using xyz.Client.DataModels.Log;
using xyz.Client.DataModels.Rpc;

namespace xyz._35021.Client;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局兜底：命令内未处理的异常（如后端不在线）写进日志下拉框，不带崩界面。
        DispatcherUnhandledException += (_, args) =>
        {
            ClientLog.Error("Client", args.Exception.Message);
            args.Handled = true;
        };

        // gRPC 通道与事件流泵必须在窗口/ViewModel 创建之前初始化。
        GrpcClientFactory.Initialize();
        RemoteEventBus.Initialize();

        base.OnStartup(e);
    }
}
