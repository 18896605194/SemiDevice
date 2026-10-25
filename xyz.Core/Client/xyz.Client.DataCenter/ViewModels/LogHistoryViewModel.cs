using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using xyz.Client.Common.Events;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.DataCenter.ViewModels;

/// <summary>
/// 日志历史页 ViewModel：按时间段（精确到分钟）查后端主日志文件，可按模块、级别、关键字筛；一次最多显示多少条按后端 machine.xml 配置，超出只留最新的。
/// </summary>
public class LogHistoryViewModel : BaseViewModel
{
    #region Column

    /// <summary>
    /// 查到的日志，最新的在最前。
    /// </summary>
    public ObservableCollection<LogModel> Logs { get; } = [];

    /// <summary>
    /// 模块下拉框选项："全部" + 日志的第一层（后端按 machine.xml 给，连上后端时取）
    /// </summary>
    public ObservableCollection<string> Modules { get; } = [LevelOptions.All];

    private string _selectedModule = LevelOptions.All;

    public string SelectedModule
    {
        get => _selectedModule;
        set => SetProperty(ref _selectedModule, value);
    }

    /// <summary>
    /// 级别下拉框选项。
    /// </summary>
    public IReadOnlyList<string> Levels => LevelOptions.Log;

    private DateTime? _startTime = QueryDateRange.Today().Start;

    /// <summary>
    /// 起始时刻（精确到分钟）。
    /// </summary>
    public DateTime? StartTime
    {
        get => _startTime;
        set => SetProperty(ref _startTime, value);
    }

    private DateTime? _endTime = QueryDateRange.Today().End;

    /// <summary>
    /// 截止时刻（精确到分钟，这一分钟整分钟都算进去）。
    /// </summary>
    public DateTime? EndTime
    {
        get => _endTime;
        set => SetProperty(ref _endTime, value);
    }

    private string _selectedLevel = LevelOptions.All;

    public string SelectedLevel
    {
        get => _selectedLevel;
        set => SetProperty(ref _selectedLevel, value);
    }

    private string _keyword = string.Empty;

    /// <summary>
    /// 关键字：模块、部件或内容里包含（不分大小写）。
    /// </summary>
    public string Keyword
    {
        get => _keyword;
        set => SetProperty(ref _keyword, value);
    }

    private string _summary = L10n.Get("common.query_hint");

    /// <summary>
    /// 查询结果说明：共多少条，或提示只显示了最新的一部分。
    /// </summary>
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    #endregion

    #region Command

    public IAsyncRelayCommand QueryCommand { get; }

    public IAsyncRelayCommand TodayCommand { get; }

    public IAsyncRelayCommand LastWeekCommand { get; }

    #endregion

    #region Service

    private readonly ILogService _service;

    #endregion

    public LogHistoryViewModel()
    {
        _service = GrpcClientFactory.Create<ILogService>();

        QueryCommand = new AsyncRelayCommand(DoQuery);
        TodayCommand = new AsyncRelayCommand(DoToday);
        LastWeekCommand = new AsyncRelayCommand(DoLastWeek);
    }

    public override void Init()
    {
        RemoteEventBus.ConnectionChanged -= OnConnectionChanged;
        RemoteEventBus.ConnectionChanged += OnConnectionChanged;
        if (RemoteEventBus.IsConnected)
        {
            OnConnectionChanged(true);
        }
    }

    /// <summary>
    /// 连上后端时取模块下拉的选项（换了 machine.xml 重开后端，选项跟着变）
    /// </summary>
    private async void OnConnectionChanged(bool connected)
    {
        if (!connected)
        {
            return;
        }

        try
        {
            var response = await _service.GetSettingsAsync(new RpcRequest());
            if (!response.Success)
            {
                return;
            }

            var settings = JsonHelper.Deserialize<LogSettingsDto>(response.Data);
            if (settings is null)
            {
                return;
            }

            string selected = SelectedModule;
            Modules.Clear();
            Modules.Add(LevelOptions.All);
            foreach (string module in settings.Modules)
            {
                Modules.Add(module);
            }

            if (Modules.Contains(selected))
            {
                SelectedModule = selected;
            }
            else
            {
                SelectedModule = LevelOptions.All;
            }
        }
        catch
        {
            // 后端不可用：断线重连时还会再取。
        }
    }

    private async Task DoQuery()
    {
        var range = QueryDateRange.Of(StartTime ?? QueryDateRange.Today().Start, EndTime ?? QueryDateRange.Today().End);
        try
        {
            var response = await _service.QueryHistoryAsync(new LogHistoryQuery
            {
                Start = range.Start,
                End = range.End,
                Module = LevelOptions.ToQuery(SelectedModule),
                Level = LevelOptions.ToQuery(SelectedLevel),
                Keyword = Keyword.Trim(),
            });

            if (!response.Success)
            {
                Summary = L10n.Get("common.query_failed");
                ClientLog.Error("DataCenter", $"日志查询失败：{L10n.Get(response.Code, response.Args)}");
                return;
            }

            var result = response.DeserializeData<HistoryResult<LogDto>>();
            Logs.Clear();
            foreach (var dto in result.Items)
            {
                Logs.Add(LogModel.From(dto));
            }

            Summary = result.Truncated
                ? L10n.Get("common.truncated", Logs.Count)
                : L10n.Get("common.total", Logs.Count);
        }
        catch (Exception exception)
        {
            Summary = L10n.Get("common.query_failed");
            ClientLog.Error("DataCenter", $"日志查询失败：{exception.Message}");
        }
    }

    private Task DoToday()
    {
        (StartTime, EndTime) = QueryDateRange.Today();
        return DoQuery();
    }

    private Task DoLastWeek()
    {
        (StartTime, EndTime) = QueryDateRange.LastDays(7);
        return DoQuery();
    }
}
