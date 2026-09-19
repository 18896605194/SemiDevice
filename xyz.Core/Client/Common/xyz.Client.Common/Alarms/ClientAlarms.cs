using xyz.Client.Common.Events;
using xyz.Client.Common.Rpc;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.Common.Alarms;

/// <summary>
/// 客户端的当前报警：连上后端时拉一次全量，之后按事件流（AlarmDto）逐条增删。
/// 顶栏报警栏、实时报警页等共用这一份，不各自订阅、各自拉。
/// 全部在 UI 线程上动（事件流回调和连接状态本来就投递到 UI 线程），Changed 也在 UI 线程触发。
/// </summary>
public static class ClientAlarms
{
    /// <summary>
    /// 当前报警，最新的在最前。
    /// </summary>
    private static readonly List<AlarmDto> Items = [];

    private static IDisposable? _subscription;

    /// <summary>
    /// 当前报警快照，最新的在最前。
    /// </summary>
    public static IReadOnlyList<AlarmDto> Active => Items.ToArray();

    /// <summary>
    /// 当前报警有变化（报出、清除、重新拉全量）。
    /// </summary>
    public static event Action? Changed;

    /// <summary>
    /// App 启动时（RemoteEventBus.Initialize 之后）调一次。
    /// </summary>
    public static void Initialize()
    {
        if (_subscription is not null)
        {
            return;
        }

        _subscription = EventBus.Register<AlarmDto>(AlarmDto.EventToken, OnAlarmChanged);
        RemoteEventBus.ConnectionChanged += OnConnectionChanged;
        if (RemoteEventBus.IsConnected)
        {
            OnConnectionChanged(true);
        }
    }

    /// <summary>
    /// 连上后端时拉全量，整表替换（事件流断过也能对上账）。
    /// </summary>
    private static async void OnConnectionChanged(bool connected)
    {
        if (!connected)
        {
            return;
        }

        try
        {
            var response = await GrpcClientFactory.Create<IAlarmService>().GetActiveAsync(new RpcRequest());
            var alarms = response.DeserializeData<List<AlarmDto>>();

            Items.Clear();
            Items.AddRange(alarms.OrderByDescending(alarm => alarm.RaisedAt));
            Changed?.Invoke();
        }
        catch
        {
            // 后端不可用：断线重连时还会再拉。
        }
    }

    /// <summary>
    /// 报出：没有就加在最前；清除：拿掉。
    /// </summary>
    private static void OnAlarmChanged(AlarmDto dto)
    {
        var index = Items.FindIndex(alarm => alarm.Source == dto.Source && alarm.Code == dto.Code);
        if (dto.IsActive)
        {
            if (index >= 0)
            {
                return;
            }

            Items.Insert(0, dto);
        }
        else
        {
            if (index < 0)
            {
                return;
            }

            Items.RemoveAt(index);
        }

        Changed?.Invoke();
    }
}
