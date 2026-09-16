using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Manual.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz._35021.Client.Manual.ViewModels;

public class TransferViewModel : BaseViewModel, IDisposable
{
    #region Column

    /// <summary>
    /// 机械手显示模型，界面上的 Robot 控件直接绑它。
    /// </summary>
    public RobotModel Robot { get; } = new();

    #endregion

    #region Service

    /// <summary>
    /// 读不到后端机械手列表时，按这个模块名订阅状态推送。
    /// </summary>
    private const string DefaultModuleName = "Robot1";

    private readonly IRobotService _service;

    private IDisposable? _stateSubscription;

    #endregion

    public TransferViewModel()
    {
        _service = GrpcClientFactory.Create<IRobotService>();
    }

    public override void Init()
    {
        string moduleName = DefaultModuleName;
        try
        {
            // module 传空串 = 返回全部 Robot；只有一个时返回单对象，多个返回数组。界面先接第一台。
            var response = _service.GetStateAsync(string.Empty).GetAwaiter().GetResult();
            response.EnsureSuccess();

            var json = response.Data?.TrimStart() ?? string.Empty;
            var robot = json.StartsWith('[')
                ? JsonHelper.Deserialize<List<RobotDto>>(json)?.FirstOrDefault()
                : JsonHelper.Deserialize<RobotDto>(json);
            if (robot is not null && !string.IsNullOrEmpty(robot.Name))
            {
                moduleName = robot.Name;
                Robot.Update(robot);
            }
        }
        catch (Exception exception)
        {
            // 读不到也不能让页面崩：记日志，状态推送到了会自动刷新。
            ClientLog.Error(nameof(TransferViewModel), $"读取机械手状态失败：{exception.Message}");
        }

        _stateSubscription?.Dispose();
        _stateSubscription = EventBus.Register<RobotDto>(moduleName, Robot.Update);
    }

    public void Dispose()
    {
        _stateSubscription?.Dispose();
        _stateSubscription = null;
    }
}
