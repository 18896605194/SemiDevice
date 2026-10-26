using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataCenter.Models;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Localization;
using xyz.Client.Presentation.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;

namespace xyz.Client.DataCenter.ViewModels;

/// <summary>
/// Job 历史页 ViewModel：按时间段查库里的 CJ（最新的在前），选中一个 CJ 取它下面的 PJ，
/// 选中 PJ 列它的每一片，选中一片列它的任务。一次最多显示多少个 CJ 按后端 machine.xml 配置。
/// </summary>
public class JobHistoryViewModel : BaseViewModel
{
    private const string LogModule = "DataCenter";

    #region Column

    /// <summary>
    /// 查到的 CJ，最新的在前
    /// </summary>
    public ObservableCollection<ControlJobHistoryModel> ControlJobs { get; } = [];

    /// <summary>
    /// 选中 CJ 下面的 PJ
    /// </summary>
    public ObservableCollection<ProcessJobHistoryModel> ProcessJobs { get; } = [];

    /// <summary>
    /// 选中 PJ 的每一片
    /// </summary>
    public ObservableCollection<JobWaferHistoryModel> Wafers { get; } = [];

    /// <summary>
    /// 选中那一片的任务
    /// </summary>
    public ObservableCollection<JobTaskHistoryModel> Tasks { get; } = [];

    private DateTime? _startTime = QueryDateRange.Today().Start;

    /// <summary>
    /// 起始时刻（精确到分钟）
    /// </summary>
    public DateTime? StartTime
    {
        get => _startTime;
        set => SetProperty(ref _startTime, value);
    }

    private DateTime? _endTime = QueryDateRange.Today().End;

    /// <summary>
    /// 截止时刻（精确到分钟，这一分钟整分钟都算进去）
    /// </summary>
    public DateTime? EndTime
    {
        get => _endTime;
        set => SetProperty(ref _endTime, value);
    }

    private string _keyword = string.Empty;

    /// <summary>
    /// 关键字：CJ 名、LotID、载具号或 LoadPort 里包含
    /// </summary>
    public string Keyword
    {
        get => _keyword;
        set => SetProperty(ref _keyword, value);
    }

    private string _summary = L10n.Get("common.query_hint");

    /// <summary>
    /// 查询结果说明：共多少个，或提示只显示了最新的一部分
    /// </summary>
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    private ControlJobHistoryModel? _selectedControlJob;

    /// <summary>
    /// 选中的 CJ：一换就去取它下面的 PJ
    /// </summary>
    public ControlJobHistoryModel? SelectedControlJob
    {
        get => _selectedControlJob;
        set
        {
            if (SetProperty(ref _selectedControlJob, value))
            {
                _ = LoadProcessJobs(value);
            }
        }
    }

    private ProcessJobHistoryModel? _selectedProcessJob;

    /// <summary>
    /// 选中的 PJ：一换就列它的每一片，默认选第一片
    /// </summary>
    public ProcessJobHistoryModel? SelectedProcessJob
    {
        get => _selectedProcessJob;
        set
        {
            if (SetProperty(ref _selectedProcessJob, value))
            {
                Wafers.Clear();
                if (value is not null)
                {
                    foreach (var wafer in value.Wafers)
                    {
                        Wafers.Add(wafer);
                    }
                }

                SelectedWafer = Wafers.FirstOrDefault();
            }
        }
    }

    private JobWaferHistoryModel? _selectedWafer;

    /// <summary>
    /// 选中的那一片：一换就列它的任务
    /// </summary>
    public JobWaferHistoryModel? SelectedWafer
    {
        get => _selectedWafer;
        set
        {
            if (SetProperty(ref _selectedWafer, value))
            {
                Tasks.Clear();
                if (value is not null)
                {
                    foreach (var task in value.Tasks)
                    {
                        Tasks.Add(task);
                    }
                }
            }
        }
    }

    #endregion

    #region Command

    public IAsyncRelayCommand QueryCommand { get; }

    public IAsyncRelayCommand TodayCommand { get; }

    public IAsyncRelayCommand LastWeekCommand { get; }

    #endregion

    #region Service

    private readonly IJobService _service;

    /// <summary>
    /// 取 PJ 的第几次：选中的 CJ 换得快时，晚回来的旧结果不要
    /// </summary>
    private int _loadVersion;

    #endregion

    public JobHistoryViewModel()
    {
        _service = GrpcClientFactory.Create<IJobService>();

        QueryCommand = new AsyncRelayCommand(DoQuery);
        TodayCommand = new AsyncRelayCommand(DoToday);
        LastWeekCommand = new AsyncRelayCommand(DoLastWeek);
    }

    private async Task DoQuery()
    {
        var range = QueryDateRange.Of(StartTime ?? QueryDateRange.Today().Start, EndTime ?? QueryDateRange.Today().End);
        try
        {
            var response = await _service.QueryHistoryAsync(RpcRequest.Of(
                (RpcParams.Start, range.Start),
                (RpcParams.End, range.End),
                (RpcParams.Keyword, Keyword.Trim())));

            if (!response.Success)
            {
                Summary = L10n.Get("common.query_failed");
                ClientLog.Error(LogModule, L10n.Get("jobhistory.query_failed", ErrorText.Of(response)));
                return;
            }

            var result = response.DeserializeData<HistoryResult<ControlJobHistoryDto>>();
            ControlJobs.Clear();
            foreach (var dto in result.Items)
            {
                ControlJobs.Add(new ControlJobHistoryModel(dto));
            }

            if (result.Truncated)
            {
                Summary = L10n.Get("common.truncated", ControlJobs.Count);
            }
            else
            {
                Summary = L10n.Get("common.total", ControlJobs.Count);
            }

            SelectedControlJob = ControlJobs.FirstOrDefault();
        }
        catch (Exception exception)
        {
            Summary = L10n.Get("common.query_failed");
            ClientLog.Error(LogModule, L10n.Get("jobhistory.query_failed", exception.Message));
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

    /// <summary>
    /// 取一个 CJ 下面的 PJ，取到默认选第一个；没选 CJ 就清空下面三块
    /// </summary>
    private async Task LoadProcessJobs(ControlJobHistoryModel? controlJob)
    {
        int version = ++_loadVersion;
        ProcessJobs.Clear();
        SelectedProcessJob = null;
        if (controlJob is null)
        {
            return;
        }

        try
        {
            var request = RpcRequest.Of((RpcParams.ControlJob, controlJob.Id.ToString()));
            var response = await _service.GetHistoryProcessJobsAsync(request);
            if (version != _loadVersion)
            {
                return;
            }

            if (!response.Success)
            {
                ClientLog.Error(LogModule, L10n.Get("jobhistory.detail_failed", controlJob.Name, ErrorText.Of(response)));
                return;
            }

            foreach (var dto in response.DeserializeData<List<ProcessJobDto>>())
            {
                ProcessJobs.Add(new ProcessJobHistoryModel(dto));
            }

            SelectedProcessJob = ProcessJobs.FirstOrDefault();
        }
        catch (Exception exception)
        {
            if (version == _loadVersion)
            {
                ClientLog.Error(LogModule, L10n.Get("jobhistory.detail_failed", controlJob.Name, exception.Message));
            }
        }
    }
}
