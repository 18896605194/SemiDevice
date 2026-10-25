using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace xyz.Client.Presentation.Models;

/// <summary>
/// 一件要等的事（查询、导出……）的进度：页面上的 ProgressOverlay 绑它，VM 用 RunAsync 跑
/// </summary>
public sealed class ProgressState : ObservableObject
{
    /// <summary>
    /// 第几次跑：后一次顶掉前一次时，前一次收尾不去关后一次开着的弹框、不改它的进度
    /// </summary>
    private int _version;

    /// <summary>
    /// 正在跑的那次的取消，没在跑为 null
    /// </summary>
    private CancellationTokenSource? _cancel;

    public ProgressState()
    {
        CancelCommand = new RelayCommand(DoCancel, CanCancel);
    }

    private bool _isOpen;

    /// <summary>
    /// 正在跑：RunAsync 开始时打开、结束时关上
    /// </summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    private double _value;

    /// <summary>
    /// 进度 0～100
    /// </summary>
    public double Value
    {
        get => _value;
        private set => SetProperty(ref _value, value);
    }

    /// <summary>
    /// 弹框上的「取消」
    /// </summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>
    /// 弹出 → 跑 work → 关掉；被取消（点了取消或 token 取消了）返回 false，别的异常照抛。
    /// 在界面线程上调：work 在后台 Report 的进度会自动回到界面线程。
    /// </summary>
    public async Task<bool> RunAsync(Func<IProgress<double>, CancellationToken, Task> work, CancellationToken token = default)
    {
        int version = Interlocked.Increment(ref _version);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        _cancel = cancel;
        Value = 0;
        IsOpen = true;
        CancelCommand.NotifyCanExecuteChanged();
        var progress = new Progress<double>(value =>
        {
            if (version == Volatile.Read(ref _version))
            {
                Value = Math.Clamp(value, 0, 100);
            }
        });
        try
        {
            await work(progress, cancel.Token);
            return true;
        }
        catch (Exception) when (cancel.IsCancellationRequested)
        {
            // 取消时 gRPC 抛的是 RpcException（Cancelled），不一定是 OperationCanceledException，按"是不是取消了"认
            return false;
        }
        finally
        {
            if (version == Volatile.Read(ref _version))
            {
                _cancel = null;
                IsOpen = false;
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private void DoCancel()
    {
        _cancel?.Cancel();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private bool CanCancel()
    {
        var cancel = _cancel;
        return cancel is not null && !cancel.IsCancellationRequested;
    }
}
