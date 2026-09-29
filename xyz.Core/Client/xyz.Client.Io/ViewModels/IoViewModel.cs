using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Io.Models;
using xyz.Client.Presentation.Localization;
using xyz.Shared.Dtos;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.Io.ViewModels;

/// <summary>
/// 一个模块的 IO 页面：点位值只订阅后端推来的整包，自己不拉也不轮询。
///
/// 一个模块一个页面（菜单 Io 下的二级项），页面里按 DI / DO / AI / AO 四类摆开。
/// 有哪些点全看后端推的点表——点表里给这个模块加一行，界面上就多一行，这儿不用改。
/// 输出点可以手动写：DO 开 / 关，AO 输入设定值下发。写进 PLC 即回包，显示以下一包推送的回读为准。
/// </summary>
public class IoViewModel : BaseViewModel, IDisposable
{
    #region Column

    private string _searchText = string.Empty;

    /// <summary>按点号、点名、描述过滤；点多了靠它找。</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                Refresh();
            }
        }
    }

    private bool _isCollecting;

    /// <summary>采集在不在工作（PLC 连着才算），界面上那个状态点看它。</summary>
    public bool IsCollecting
    {
        get => _isCollecting;
        set => SetProperty(ref _isCollecting, value);
    }

    public ObservableCollection<IoPointModel> DiPoints { get; } = [];

    public ObservableCollection<IoPointModel> DoPoints { get; } = [];

    public ObservableCollection<IoPointModel> AiPoints { get; } = [];

    public ObservableCollection<IoPointModel> AoPoints { get; } = [];

    public ICollectionView DiView { get; }

    public ICollectionView DoView { get; }

    public ICollectionView AiView { get; }

    public ICollectionView AoView { get; }

    #endregion

    #region Command

    /// <summary>DO 置 ON（参数：那一行的点）。</summary>
    public IAsyncRelayCommand<IoPointModel> TurnOnCommand { get; }

    /// <summary>DO 置 OFF（参数：那一行的点）。</summary>
    public IAsyncRelayCommand<IoPointModel> TurnOffCommand { get; }

    /// <summary>AO 下发那一行输入框里的设定值（参数：那一行的点）。</summary>
    public IAsyncRelayCommand<IoPointModel> SendAoCommand { get; }

    #endregion

    #region Service

    /// <summary>本页对应的模块名，跟点表 Module 列、sc.xml 里的模块名对齐。</summary>
    private readonly string _module;

    private readonly IIoService _service;

    private IDisposable? _subscription;

    #endregion

    public IoViewModel(string module)
    {
        _module = module;
        _service = GrpcClientFactory.Create<IIoService>();

        DiView = CreateView(DiPoints);
        DoView = CreateView(DoPoints);
        AiView = CreateView(AiPoints);
        AoView = CreateView(AoPoints);

        TurnOnCommand = new AsyncRelayCommand<IoPointModel>(DoTurnOn);
        TurnOffCommand = new AsyncRelayCommand<IoPointModel>(DoTurnOff);
        SendAoCommand = new AsyncRelayCommand<IoPointModel>(DoSendAo);
    }

    public override void Init()
    {
        _subscription?.Dispose();
        _subscription = EventBus.Register<IoDto>(IoDto.EventToken, OnSnapshotReceived);
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    #region 收包

    /// <summary>
    /// 收到一包：点数对得上就只改值，对不上才重建。
    /// 点表只在启动时定，所以正常情况下每包都只是改值——每半秒重建一次表格，
    /// 选中行和滚动位置就没法看了，AO 输入框里正在输的数也会被冲掉。
    /// </summary>
    private void OnSnapshotReceived(IoDto dto)
    {
        IsCollecting = dto.IsCollecting;

        Apply(DiPoints, Read(dto, "DI"));
        Apply(DoPoints, Read(dto, "DO"));
        Apply(AiPoints, Read(dto, "AI"));
        Apply(AoPoints, Read(dto, "AO"));
    }

    private void Apply(ObservableCollection<IoPointModel> target, IReadOnlyList<IoPointDto> points)
    {
        if (target.Count != points.Count)
        {
            Fill(target, points);
            return;
        }

        for (int i = 0; i < points.Count; i++)
        {
            var row = target[i];
            var point = points[i];
            if (row.Index != point.Index || row.Name != point.Name)
            {
                Fill(target, points);
                return;
            }

            row.IsOn = point.IsOn;
            row.Value = point.Value;
            row.IsValid = point.IsValid;
        }
    }

    private void Fill(ObservableCollection<IoPointModel> target, IReadOnlyList<IoPointDto> points)
    {
        target.Clear();
        foreach (var point in points)
        {
            target.Add(new IoPointModel
            {
                Index = point.Index,
                Name = point.Name,
                Component = point.Component,
                Description = point.Description,
                Unit = point.Unit,
                IsOutput = point.IsOutput,
                IsOn = point.IsOn,
                Value = point.Value,
                IsValid = point.IsValid,
            });
        }

        Refresh();
    }

    /// <summary>从整包里挑出本模块这一类的点；点表里没有这个模块就是空表。</summary>
    private IReadOnlyList<IoPointDto> Read(IoDto dto, string type)
    {
        return dto.Types
            .FirstOrDefault(item => string.Equals(item.Type, type, StringComparison.OrdinalIgnoreCase))?
            .Modules
            .FirstOrDefault(item => string.Equals(item.Module, _module, StringComparison.OrdinalIgnoreCase))?
            .Points ?? [];
    }

    #endregion

    #region 写输出

    private Task DoTurnOn(IoPointModel? point)
    {
        return WriteDo(point, true);
    }

    private Task DoTurnOff(IoPointModel? point)
    {
        return WriteDo(point, false);
    }

    /// <summary>
    /// 写一个 DO。开、关是两个按钮而不是一个切换：按下去是什么结果一眼确定，不依赖屏幕上那一拍的回读。
    /// </summary>
    private async Task WriteDo(IoPointModel? point, bool on)
    {
        if (point is null)
        {
            return;
        }

        var response = await _service.WriteDoAsync(new IoWriteRequest { Index = point.Index, Value = on ? 1 : 0 });
        if (!response.Success)
        {
            ClientLog.Error(_module, L10n.Get(response.Code, response.Args));
        }
    }

    /// <summary>
    /// 下发一个 AO：输入框里的数按工程值下发，超出点表标定范围后端会拒。输入框里的数下发后留着，方便微调再发。
    /// </summary>
    private async Task DoSendAo(IoPointModel? point)
    {
        if (point is null)
        {
            return;
        }

        if (!TryParseValue(point.PendingValue, out double value))
        {
            ClientLog.Warn(_module, L10n.Get("io.invalid_value", point.PendingValue));
            return;
        }

        var response = await _service.WriteAoAsync(new IoWriteRequest { Index = point.Index, Value = value });
        if (!response.Success)
        {
            ClientLog.Error(_module, L10n.Get(response.Code, response.Args));
        }
    }

    /// <summary>小数点写成点或当前区域的写法都认。</summary>
    private static bool TryParseValue(string text, out double value)
    {
        text = text.Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    #endregion

    #region 搜索

    private ICollectionView CreateView(ObservableCollection<IoPointModel> points)
    {
        return new ListCollectionView(points) { Filter = Matches };
    }

    private bool Matches(object item)
    {
        if (item is not IoPointModel point)
        {
            return false;
        }

        string query = SearchText.Trim();
        return query.Length == 0
               || point.Index.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
               || point.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
               || point.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void Refresh()
    {
        DiView.Refresh();
        DoView.Refresh();
        AiView.Refresh();
        AoView.Refresh();
    }

    #endregion
}
