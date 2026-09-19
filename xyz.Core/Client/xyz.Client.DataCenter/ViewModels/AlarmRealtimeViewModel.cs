using System.Collections.ObjectModel;
using Mapster;
using xyz.Client.Common.Events;
using xyz.Client.Common.Rpc;
using xyz.Client.DataCenter.Models;
using xyz.Client.DataModels.ViewModels;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.DataCenter.ViewModels;

/// <summary>
/// 实时报警页 ViewModel（只显示）：连上后端时拉一次当前报警，之后报出、清除经事件流逐条到达，最新的在最上面。
/// </summary>
public class AlarmRealtimeViewModel : BaseViewModel
{
    #region Column

    /// <summary>
    /// 当前报警，最新的在最前。
    /// </summary>
    public ObservableCollection<AlarmModel> Alarms { get; } = [];

    /// <summary>
    /// 标题栏右侧的状态。
    /// </summary>
    public string Summary => $"当前报警 {Alarms.Count} 条";

    #endregion

    #region Service

    private readonly IAlarmService _service;

    private IDisposable? _subscription;

    #endregion

    public AlarmRealtimeViewModel()
    {
        _service = GrpcClientFactory.Create<IAlarmService>();
        Alarms.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Summary));
    }

    public override void Init()
    {
        _subscription?.Dispose();
        _subscription = EventBus.Register<AlarmDto>(AlarmDto.EventToken, OnAlarmChanged);

        RemoteEventBus.ConnectionChanged -= OnConnectionChanged;
        RemoteEventBus.ConnectionChanged += OnConnectionChanged;
        if (RemoteEventBus.IsConnected)
        {
            OnConnectionChanged(true);
        }
    }

    /// <summary>
    /// 连上后端时拉当前报警，整表替换（事件流断过也能对上账）。
    /// </summary>
    private async void OnConnectionChanged(bool connected)
    {
        if (!connected)
        {
            return;
        }

        try
        {
            var response = await _service.GetActiveAsync(new RpcRequest());
            var alarms = response.DeserializeData<List<AlarmDto>>();

            Alarms.Clear();
            foreach (var dto in alarms.OrderByDescending(alarm => alarm.RaisedAt))
            {
                Alarms.Add(dto.Adapt<AlarmModel>());
            }
        }
        catch
        {
            // 后端不可用：断线重连时还会再拉。
        }
    }

    /// <summary>
    /// 报出：表里没有就加在最前；清除：从表里拿掉。
    /// </summary>
    private void OnAlarmChanged(AlarmDto dto)
    {
        var existing = Alarms.FirstOrDefault(alarm => alarm.Source == dto.Source && alarm.Code == dto.Code);
        if (dto.IsActive)
        {
            if (existing is null)
            {
                Alarms.Insert(0, dto.Adapt<AlarmModel>());
            }

            return;
        }

        if (existing is not null)
        {
            Alarms.Remove(existing);
        }
    }
}
