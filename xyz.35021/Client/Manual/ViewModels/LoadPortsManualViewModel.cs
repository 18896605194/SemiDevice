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
/// 35021 大手动界面 ViewModel：启动时从后端读取 sc.xml 里配置的全部 LoadPort，
/// 有几个就组装几个平台的手动操作面板。
/// </summary>
public class LoadPortsManualViewModel : BaseViewModel
{
    #region Column

    /// <summary>
    /// 全部 LoadPort 的模块名（如 LoadPort1 / LoadPort2），顺序即后端返回顺序。
    /// </summary>
    public ObservableCollection<string> Ports { get; } = new();

    #endregion

    #region Command

    #endregion

    #region Service

    private readonly ILoadPortService _service;

    #endregion

    public LoadPortsManualViewModel()
    {
        _service = GrpcClientFactory.Create<ILoadPortService>();
    }

    public override void Init()
    {
        Ports.Clear();

        try
        {
            // module 传空串 = 返回全部 LoadPort。注意后端约定：只有一个时返回单对象，其余返回数组。
            var response = _service.GetStateAsync(string.Empty).GetAwaiter().GetResult();
            response.EnsureSuccess();

            var json = response.Data?.TrimStart() ?? string.Empty;
            var ports = json.StartsWith('[')
                ? JsonHelper.Deserialize<List<LoadPortDto>>(json) ?? new List<LoadPortDto>()
                : new List<LoadPortDto> { JsonHelper.Deserialize<LoadPortDto>(json)! };

            foreach (var port in ports)
            {
                Ports.Add(port.Name);
            }
        }
        catch (Exception exception)
        {
            // 页面不能因为读不到列表就崩：记日志、界面为空，后端恢复后重启客户端即可。
            ClientLog.Error(nameof(LoadPortsManualViewModel), $"读取 LoadPort 列表失败：{exception.Message}");
        }
    }
}
