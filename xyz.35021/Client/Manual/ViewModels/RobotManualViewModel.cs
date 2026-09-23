using System.Collections.ObjectModel;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz._35021.Client.Manual.ViewModels;

/// <summary>
/// 35021 Robot 手动界面 ViewModel：启动时从后端读取 sc.xml 里配置的全部 Robot，
/// 有几个就组装几个平台的机械手手动操作面板。
/// </summary>
public class RobotManualViewModel : BaseViewModel
{
    #region Column

    /// <summary>
    /// 全部 Robot 的模块名（如 Robot1），顺序即后端返回顺序。
    /// </summary>
    public ObservableCollection<string> Robots { get; } = new();

    #endregion

    #region Command

    #endregion

    #region Service

    private readonly IRobotService _service;

    #endregion

    public RobotManualViewModel()
    {
        _service = GrpcClientFactory.Create<IRobotService>();
    }

    public override void Init()
    {
        Robots.Clear();

        try
        {
            // module 传空串 = 返回全部 Robot。注意后端约定：只有一个时返回单对象，其余返回数组。
            var response = _service.GetStateAsync(string.Empty).GetAwaiter().GetResult();
            response.EnsureSuccess();

            var json = response.Data?.TrimStart() ?? string.Empty;
            var robots = json.StartsWith('[')
                ? JsonHelper.Deserialize<List<RobotDto>>(json) ?? new List<RobotDto>()
                : new List<RobotDto> { JsonHelper.Deserialize<RobotDto>(json)! };

            foreach (var robot in robots)
            {
                Robots.Add(robot.Name);
            }
        }
        catch (Exception exception)
        {
            // 页面不能因为读不到列表就崩：记日志、界面为空，后端恢复后重启客户端即可。
            ClientLog.Error(nameof(RobotManualViewModel), $"读取 Robot 列表失败：{exception.Message}");
        }
    }
}
