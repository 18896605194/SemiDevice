using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using xyz.Client.Common.Events;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Services;
using xyz.Tools;
using xyz.Client.Presentation.Localization;

namespace xyz.Client.DataCenter.ViewModels;

/// <summary>
/// 实时日志页 ViewModel：后端（设备）日志经事件流实时到达，最新的在最上面；连上后端时补拉最近一段。
/// 可按级别、关键字筛；可暂停，暂停期间新到的先攒着，继续时一次补上。
/// 客户端自己的报错不在这里，看顶栏日志下拉框。
/// </summary>
public class LogRealtimeViewModel : BaseViewModel
{
    #region Column

    /// <summary>
    /// 日志，最新的在最前。
    /// </summary>
    public ObservableCollection<LogModel> Logs { get; } = [];

    /// <summary>
    /// 按级别、关键字筛过的视图，表格绑这个。
    /// </summary>
    public ICollectionView LogsView { get; }

    /// <summary>
    /// 级别下拉框选项。
    /// </summary>
    public IReadOnlyList<string> Levels => LevelOptions.Log;

    private string _selectedLevel = LevelOptions.All;

    public string SelectedLevel
    {
        get => _selectedLevel;
        set
        {
            if (SetProperty(ref _selectedLevel, value))
            {
                LogsView.Refresh();
            }
        }
    }

    private string _keyword = string.Empty;

    /// <summary>
    /// 关键字：模块名或内容里包含就显示（不分大小写）。
    /// </summary>
    public string Keyword
    {
        get => _keyword;
        set
        {
            if (SetProperty(ref _keyword, value))
            {
                LogsView.Refresh();
            }
        }
    }

    private bool _isPaused;

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetProperty(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(PauseText));
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    /// <summary>
    /// 暂停按钮上的字。
    /// </summary>
    public string PauseText => L10n.Get(IsPaused ? "log.resume" : "log.pause");

    /// <summary>
    /// 工具栏右侧的状态：共多少条；暂停时显示攒了多少条没显示。
    /// </summary>
    public string Summary => IsPaused ? L10n.Get("log.paused_summary", _pending.Count) : L10n.Get("common.total", Logs.Count);

    #endregion

    #region Command

    public IRelayCommand PauseCommand { get; }

    public IRelayCommand ClearCommand { get; }

    #endregion

    #region Service

    private readonly ILogService _service;

    /// <summary>
    /// 暂停期间新到的日志，按到达顺序。
    /// </summary>
    private readonly List<LogModel> _pending = [];

    private IDisposable? _subscription;

    /// <summary>
    /// 页面最多保留的日志条数，超出丢最旧的；连上后端时按 sc.xml 的 Log 节点（RealtimeDisplayMaxCount）更新。
    /// </summary>
    private int _maxLogs = LogSettingsDto.DefaultRealtimeDisplayMaxCount;

    #endregion

    public LogRealtimeViewModel()
    {
        _service = GrpcClientFactory.Create<ILogService>();

        LogsView = CollectionViewSource.GetDefaultView(Logs);
        LogsView.Filter = Matches;
        Logs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Summary));

        PauseCommand = new RelayCommand(DoPause);
        ClearCommand = new RelayCommand(DoClear);
    }

    public override void Init()
    {
        _subscription?.Dispose();
        _subscription = EventBus.Register<LogDto>(LogDto.EventToken, dto => Receive(LogModel.From(dto)));

        RemoteEventBus.ConnectionChanged -= OnConnectionChanged;
        RemoteEventBus.ConnectionChanged += OnConnectionChanged;
        if (RemoteEventBus.IsConnected)
        {
            OnConnectionChanged(true);
        }
    }

    /// <summary>
    /// 连上后端时补拉最近一段（晚连上也能看到之前的日志）；已经在表里的不重复加。
    /// </summary>
    private async void OnConnectionChanged(bool connected)
    {
        if (!connected)
        {
            return;
        }

        await LoadSettingsAsync();

        try
        {
            // 后端缓冲里有多少补多少（缓冲条数在 sc.xml 的 Log 节点配）
            var response = await _service.GetRecentAsync(new LogQuery());
            if (!response.Success)
            {
                return;
            }

            var shown = Logs.Concat(_pending).Select(KeyOf).ToHashSet();
            foreach (var dto in JsonHelper.Deserialize<List<LogDto>>(response.Data) ?? [])
            {
                var model = LogModel.From(dto);
                if (shown.Add(KeyOf(model)))
                {
                    Receive(model);
                }
            }
        }
        catch
        {
            // 后端不可用：断线重连时还会再拉。
        }
    }

    /// <summary>
    /// 取后端配的显示条数（sc.xml 的 Log 节点），配小了就马上把多出来的旧日志丢掉；取不到保持现值。
    /// </summary>
    private async Task LoadSettingsAsync()
    {
        try
        {
            var response = await _service.GetSettingsAsync(new RpcRequest());
            var settings = response.Success ? JsonHelper.Deserialize<LogSettingsDto>(response.Data) : null;
            if (settings is null || settings.RealtimeDisplayMaxCount <= 0)
            {
                return;
            }

            _maxLogs = settings.RealtimeDisplayMaxCount;
            TrimOldest();
        }
        catch
        {
            // 后端不可用：断线重连时还会再拉。
        }
    }

    private void Receive(LogModel model)
    {
        if (IsPaused)
        {
            _pending.Add(model);
            TrimOldest();

            OnPropertyChanged(nameof(Summary));
            return;
        }

        Insert(model);
    }

    /// <summary>
    /// 超出显示条数丢最旧的：表格最新的在前、最旧的在后；暂停攒着的按到达顺序、最旧的在前。
    /// </summary>
    private void TrimOldest()
    {
        while (Logs.Count > _maxLogs)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }

        while (_pending.Count > _maxLogs)
        {
            _pending.RemoveAt(0);
        }
    }

    /// <summary>
    /// 按时间倒序插进去：实时到的最新，插在最前；补拉的历史较旧，插到后面相应位置。
    /// </summary>
    private void Insert(LogModel model)
    {
        var index = 0;
        while (index < Logs.Count && Logs[index].Time > model.Time)
        {
            index++;
        }

        Logs.Insert(index, model);
        TrimOldest();
    }

    private void DoPause()
    {
        if (!IsPaused)
        {
            IsPaused = true;
            return;
        }

        IsPaused = false;
        foreach (var model in _pending)
        {
            Insert(model);
        }

        _pending.Clear();
    }

    private void DoClear()
    {
        Logs.Clear();
        _pending.Clear();
        OnPropertyChanged(nameof(Summary));
    }

    private bool Matches(object item)
    {
        if (item is not LogModel log)
        {
            return false;
        }

        if (SelectedLevel != LevelOptions.All && log.Level != SelectedLevel)
        {
            return false;
        }

        var keyword = Keyword.Trim();
        return keyword.Length == 0
               || log.Module.Contains(keyword, StringComparison.OrdinalIgnoreCase)
               || log.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static (DateTime Time, string Module, string Message) KeyOf(LogModel model)
    {
        return (model.Time, model.Module, model.Message);
    }
}
