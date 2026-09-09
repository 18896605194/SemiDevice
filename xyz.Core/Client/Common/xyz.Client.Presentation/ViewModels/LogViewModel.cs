using System.Collections.ObjectModel;
using System.Threading;
using xyz.Client.Common.Events;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.Presentation.ViewModels;

/// <summary>
/// 日志下拉框 ViewModel。
/// 生产者（后端事件流 / 后端历史补发 / 客户端自身报错）只往队列塞；
/// 本 ViewModel 就是那个消费者：在 UI 线程 await foreach 逐条从队列取，取到一条显示一条，
/// 不轮询、不用定时器。队列有界，满了丢最旧的。
/// </summary>
public class LogViewModel : BaseViewModel, IDisposable
{
    /// <summary>
    /// 界面最多保留的日志条数，超出丢弃最旧的。
    /// </summary>
    private const int MaxLogs = 500;

    /// <summary>
    /// 连上后端时补拉的历史条数。
    /// </summary>
    private const int HistoryCount = 200;

    #region Column

    /// <summary>
    /// 下拉框列表：从队列逐条取出后追加。
    /// </summary>
    public ObservableCollection<LogModel> Logs { get; } = [];

    private LogModel? _selectedLog;

    /// <summary>
    /// 当前选中项；每取到一条新日志就指向最新一条，收起状态下下拉框显示最新日志。
    /// </summary>
    public LogModel? SelectedLog
    {
        get => _selectedLog;
        set => SetProperty(ref _selectedLog, value);
    }

    #endregion

    #region Service

    /// <summary>
    /// 当前活跃的消费者：进程内只允许一个日志 ViewModel 从队列取数，
    /// 否则多个 LogBar 同时读同一个队列会把日志拆开。
    /// </summary>
    private static LogViewModel? _active;

    private IDisposable? _subscription;
    private CancellationTokenSource? _consumeCts;
    private Task? _consumer;

    /// <summary>
    /// 消费者 Task（诊断/测试用）：从队列逐条取日志的循环。
    /// </summary>
    public Task? Consumer => _consumer;

    #endregion

    public override void Init()
    {
        var previous = Interlocked.Exchange(ref _active, this);
        if (previous is not null && !ReferenceEquals(previous, this))
        {
            previous.Dispose();
        }

        // 后端实时日志经事件流到达 → 入队（不直接更新界面）
        _subscription?.Dispose();
        _subscription = EventBus.Register<LogDto>(LogDto.EventToken, ClientLog.Enqueue);

        // 界面即消费者：在 UI 线程启动，await 恢复后自然回到 UI 线程，逐条取、逐条显示
        _consumeCts?.Cancel();
        _consumeCts?.Dispose();
        _consumeCts = new CancellationTokenSource();
        _consumer = ConsumeAsync(_consumeCts.Token);

        // 历史补发：连上后端时拉一次（晚连上也能看到之前的报错）
        RemoteEventBus.ConnectionChanged -= OnConnectionChanged;
        RemoteEventBus.ConnectionChanged += OnConnectionChanged;
        if (RemoteEventBus.IsConnected)
        {
            OnConnectionChanged(true);
        }
    }

    private async void OnConnectionChanged(bool connected)
    {
        if (!connected)
        {
            return;
        }

        try
        {
            var service = GrpcClientFactory.Create<ILogService>();
            var response = await service.GetRecentAsync(new LogQuery { Count = HistoryCount });
            if (!response.Success)
            {
                return;
            }

            var logs = JsonHelper.Deserialize<List<LogDto>>(response.Data);
            if (logs is null)
            {
                return;
            }

            // 历史同样走队列，界面照旧逐条消费
            foreach (var log in logs)
            {
                ClientLog.Enqueue(log);
            }
        }
        catch
        {
            // 后端不可用：断线重连时还会再拉
        }
    }

    private async Task ConsumeAsync(CancellationToken token)
    {
        try
        {
            await foreach (var dto in ClientLog.Reader.ReadAllAsync(token))
            {
                Append(LogModel.From(dto));

                while (Logs.Count > MaxLogs)
                {
                    Logs.RemoveAt(0);
                }

                SelectedLog = Logs[^1];
            }
        }
        catch (OperationCanceledException)
        {
            // 停止消费：窗口关闭/重新 Init
        }
    }

    /// <summary>
    /// 按时间插入：历史补发可能晚于实时日志到达，插到正确位置，保证列表始终按时间升序。
    /// </summary>
    private void Append(LogModel model)
    {
        var index = Logs.Count;
        while (index > 0 && Logs[index - 1].Time > model.Time)
        {
            index--;
        }

        if (index == Logs.Count)
        {
            Logs.Add(model);
        }
        else
        {
            Logs.Insert(index, model);
        }
    }

    public void Dispose()
    {
        Interlocked.CompareExchange(ref _active, null, this);

        RemoteEventBus.ConnectionChanged -= OnConnectionChanged;

        _consumeCts?.Cancel();
        _consumeCts?.Dispose();
        _consumeCts = null;

        _subscription?.Dispose();
        _subscription = null;
    }
}
