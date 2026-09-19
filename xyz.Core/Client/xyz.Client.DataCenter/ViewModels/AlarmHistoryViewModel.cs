using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataCenter.Models;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;

namespace xyz.Client.DataCenter.ViewModels;

/// <summary>
/// 报警历史页 ViewModel：按日期段查报警记录（报出、清除各一条），可按等级、关键字筛；最多显示最新的 1000 条。
/// </summary>
public class AlarmHistoryViewModel : BaseViewModel
{
    /// <summary>
    /// 一次最多显示的条数，超出只留最新的。
    /// </summary>
    private const int MaxCount = 1000;

    #region Column

    /// <summary>
    /// 查到的记录，最新的在最前。
    /// </summary>
    public ObservableCollection<AlarmHistoryModel> Records { get; } = [];

    /// <summary>
    /// 等级下拉框选项。
    /// </summary>
    public IReadOnlyList<string> Levels => LevelOptions.Alarm;

    private DateTime? _startDate = DateTime.Today;

    public DateTime? StartDate
    {
        get => _startDate;
        set => SetProperty(ref _startDate, value);
    }

    private DateTime? _endDate = DateTime.Today;

    public DateTime? EndDate
    {
        get => _endDate;
        set => SetProperty(ref _endDate, value);
    }

    private string _selectedLevel = LevelOptions.All;

    public string SelectedLevel
    {
        get => _selectedLevel;
        set => SetProperty(ref _selectedLevel, value);
    }

    private string _keyword = string.Empty;

    /// <summary>
    /// 关键字：来源、报警代码或报警内容里包含。
    /// </summary>
    public string Keyword
    {
        get => _keyword;
        set => SetProperty(ref _keyword, value);
    }

    private string _summary = "选好日期后点查询";

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

    private readonly IAlarmService _service;

    #endregion

    public AlarmHistoryViewModel()
    {
        _service = GrpcClientFactory.Create<IAlarmService>();

        QueryCommand = new AsyncRelayCommand(DoQuery);
        TodayCommand = new AsyncRelayCommand(DoToday);
        LastWeekCommand = new AsyncRelayCommand(DoLastWeek);
    }

    private async Task DoQuery()
    {
        var range = QueryDateRange.Of(StartDate ?? DateTime.Today, EndDate ?? DateTime.Today);
        try
        {
            var response = await _service.QueryHistoryAsync(new AlarmHistoryQuery
            {
                Start = range.Start,
                End = range.End,
                Level = LevelOptions.ToQuery(SelectedLevel),
                Keyword = Keyword.Trim(),
                MaxCount = MaxCount,
            });

            if (!response.Success)
            {
                Summary = "查询失败";
                ClientLog.Error("Alarm", $"报警历史查询失败：{L10n.Get(response.Code, response.Args)}");
                return;
            }

            var result = response.DeserializeData<HistoryResult<AlarmHistoryDto>>();
            Records.Clear();
            foreach (var dto in result.Items)
            {
                Records.Add(dto.Adapt<AlarmHistoryModel>());
            }

            Summary = result.Truncated
                ? $"只显示最新的 {MaxCount} 条，缩短日期段或加条件再查"
                : $"共 {Records.Count} 条";
        }
        catch (Exception exception)
        {
            Summary = "查询失败";
            ClientLog.Error("Alarm", $"报警历史查询失败：{exception.Message}");
        }
    }

    private Task DoToday()
    {
        StartDate = DateTime.Today;
        EndDate = DateTime.Today;
        return DoQuery();
    }

    private Task DoLastWeek()
    {
        StartDate = DateTime.Today.AddDays(-6);
        EndDate = DateTime.Today;
        return DoQuery();
    }
}
