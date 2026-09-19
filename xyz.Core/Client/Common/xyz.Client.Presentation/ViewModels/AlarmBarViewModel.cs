using System.Collections.ObjectModel;
using xyz.Client.Common.Alarms;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Models;

namespace xyz.Client.Presentation.ViewModels;

/// <summary>
/// 顶栏报警栏 ViewModel：跟着客户端当前报警（ClientAlarms）走；收起时显示最新一条，旁边显示条数。
/// </summary>
public class AlarmBarViewModel : BaseViewModel
{
    #region Column

    /// <summary>
    /// 当前报警，最新的在最前。
    /// </summary>
    public ObservableCollection<AlarmModel> Alarms { get; } = [];

    private AlarmModel? _latest;

    /// <summary>
    /// 最新一条报警；没有报警为 null（下拉框显示提示文字）。
    /// </summary>
    public AlarmModel? Latest
    {
        get => _latest;
        private set => SetProperty(ref _latest, value);
    }

    /// <summary>
    /// 有没有报警：有就显示条数，没有就显示提示。
    /// </summary>
    public bool HasAlarm => Alarms.Count > 0;

    /// <summary>
    /// 条数文字。
    /// </summary>
    public string CountText => $"{Alarms.Count} 条";

    #endregion

    public override void Init()
    {
        ClientAlarms.Changed -= Refresh;
        ClientAlarms.Changed += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        Alarms.Clear();
        foreach (var dto in ClientAlarms.Active)
        {
            Alarms.Add(AlarmModel.From(dto));
        }

        Latest = Alarms.FirstOrDefault();
        OnPropertyChanged(nameof(HasAlarm));
        OnPropertyChanged(nameof(CountText));
    }
}
