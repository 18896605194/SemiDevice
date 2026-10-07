using System.Diagnostics;
using xyz.Components.Enums;
using xyz.Shared.Errors;

namespace xyz.Components.Components;

public abstract class ModuleOperation
{
    #region Column
    /// <summary>
    /// 4种状态， 运行，成功，失败，停止
    /// </summary>
    private volatile OperationState _state = OperationState.Running;
    /// <summary>
    /// 当前状态。
    /// </summary>
    public OperationState State => _state;

    private readonly object _terminalGate = new();
    private bool _completionDeferred;

    /// <summary>
    /// 动作名eg：Load
    /// </summary>
    public string Name { get; }
    

    /// <summary>
    /// 是否已达终态。 成功失败，停止
    /// </summary>
    public bool IsTerminal => _state is OperationState.Completed or OperationState.Failed or OperationState.Aborted;

    /// <summary>
    /// 终态是否成功（Completed）。
    /// </summary>
    public bool IsSuccess => _state == OperationState.Completed;

    /// <summary>
    /// 收尾完成：不光到了终态，挂着它的模块也已经落好状态、做完收尾（记晶圆账、报警）。
    /// 别的扫描线程判"这一步做完了"要看它，不能看 <see cref="IsTerminal"/>——终态在 Complete() 那一刻就有了，
    /// 模块收尾还在后面，这时候去查账会看到"做完了、账还没动"。搬运由搬运管理在资源和设备中止收尾后确认；没有宿主的操作终态即收尾完成。
    /// </summary>
    public bool IsSettled => _terminal.IsSet;

    /// <summary>
    /// 失败/中止原因，成功时为空。仅供日志/调试，界面显示走 Code。
    /// </summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>
    /// 失败错误码
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// 错误码参数
    /// </summary>
    public IReadOnlyList<string> ErrorArgs { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// 超时账本：构造时启动，终结时停止；等待步据此判超时。
    /// </summary>
    protected Stopwatch Watch { get; } = Stopwatch.StartNew();

    #endregion

    protected ModuleOperation(string name)
    {
        Name = name;
    }

    /// <summary>
    /// 由模块扫描线程步进一拍；终态后 no-op。
    /// </summary>
    public void Scan()
    {
        if (IsTerminal)
        {
            return;
        }

        try
        {
            OnScan();
        }
        catch (Exception exception)
        {
            Fail(ErrorCodes.OperationFaulted, $"操作异常: {exception.Message}", Name, exception.Message);
        }
    }

    /// <summary>
    /// 步进逻辑（switch(Step) 推进）；只由模块扫描线程执行，内部无需加锁。
    /// </summary>
    protected abstract void OnScan();

    #region Abort
    /// <summary>
    /// 宿主（RPC 线程）同步打断：直接落 Aborted 并触发钩子，用于 Abort 顶替在途操作。
    /// </summary>
    public void AbortByHost(string reason)
    {
        if (!TrySetTerminal(OperationState.Aborted, reason, ErrorCodes.Aborted, [Name]))
        {
            return;
        }

        try
        {
            OnAborted(reason);
        }
        catch
        {
            // 钩子异常不打断宿主。
        }
    }

    /// <summary>
    /// 操作被打断后的钩子（如向设备发 ABORT 指令）。
    /// </summary>
    protected virtual void OnAborted(string reason)
    {
    }

    #endregion
  
    /// <summary>
    /// 成功终结。
    /// </summary>
    protected void Complete()
    {
        TrySetTerminal(OperationState.Completed, string.Empty, string.Empty, []);
    }

    /// <summary>
    /// 失败终结（带错误码）：界面按 Code 查语言包渲染，reason 仅供日志/调试。
    /// </summary>
    protected void Fail(string code, string reason, params string[] args)
    {
        TrySetTerminal(OperationState.Failed, reason, code, args);
    }


    /// <summary>
    /// 等待操作完成收尾（阻塞，完成事件唤醒不空转）；false 只表示等待超时。
    /// </summary>
    /// <param name="timeoutMilliseconds"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public bool WaitReply(int timeoutMilliseconds, CancellationToken cancellationToken = default)
    {
        return _terminal.Wait(timeoutMilliseconds, cancellationToken);
    }

    private readonly ManualResetEventSlim _terminal = new(false);

    /// <summary>
    /// 挂载后由模块在状态迁移完成后唤醒等待方，避免先回包再更新模块状态。
    /// </summary>
    internal void DeferCompletion()
    {
        lock (_terminalGate)
        {
            if (IsTerminal || _completionDeferred)
            {
                throw new InvalidOperationException("操作只能挂载一次，且必须处于执行中。");
            }

            _completionDeferred = true;
        }
    }

    internal void NotifyCompletion()
    {
        _terminal.Set();
    }

    private bool TrySetTerminal(OperationState state, string reason, string code, IReadOnlyList<string> args)
    {
        lock (_terminalGate)
        {
            if (IsTerminal)
            {
                return false;
            }

            Reason = reason ?? string.Empty;
            Code = code;
            ErrorArgs = Array.AsReadOnly(args.ToArray());
            Watch.Stop();
            _state = state;

            if (!_completionDeferred)
            {
                NotifyCompletion();
            }

            return true;
        }
    }
}
