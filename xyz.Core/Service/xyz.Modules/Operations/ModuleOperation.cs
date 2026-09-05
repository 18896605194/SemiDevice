using System.Diagnostics;
using System.Threading;
using xyz.Shared.Errors;

namespace xyz.Modules;

public abstract class ModuleOperation
{
    private OperationState _state = OperationState.Running;

    /// <summary>
    /// 操作名（如 "Load"），诊断用。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 当前状态。
    /// </summary>
    public OperationState State => _state;

    /// <summary>
    /// 是否已达终态。
    /// </summary>
    public bool IsTerminal => _state is OperationState.Completed
        or OperationState.Failed
        or OperationState.Aborted;

    /// <summary>
    /// 终态是否成功（Completed）。
    /// </summary>
    public bool IsSuccess => _state == OperationState.Completed;

    /// <summary>
    /// 失败/中止原因，成功时为空。仅供日志/调试，界面显示走 Code。
    /// </summary>
    public string Reason { get; private set; } = string.Empty;

    /// <summary>
    /// 失败错误码（见 xyz.Shared.Errors.ErrorCodes），前端查语言包渲染；无码为空。
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// 错误码参数，顺序见错误码定义注释。
    /// </summary>
    public IReadOnlyList<string> ErrorArgs { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// 超时账本：构造时启动，终结时停止；等待步据此判超时。
    /// </summary>
    protected Stopwatch Watch { get; } = Stopwatch.StartNew();

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
    /// 宿主（RPC 线程）同步打断：直接落 Aborted 并触发钩子，用于 Abort 顶替在途操作。
    /// </summary>
    public void AbortByHost(string reason)
    {
        if (IsTerminal)
        {
            return;
        }

        SetTerminal(OperationState.Aborted, reason);

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
    /// 步进逻辑（switch(Step) 推进）；只由模块扫描线程执行，内部无需加锁。
    /// </summary>
    protected abstract void OnScan();

    /// <summary>
    /// 操作被打断后的钩子（如向设备发 ABORT 指令）。
    /// </summary>
    protected virtual void OnAborted(string reason)
    {
    }

    /// <summary>
    /// 成功终结。
    /// </summary>
    protected void Complete()
    {
        SetTerminal(OperationState.Completed, string.Empty);
    }

    /// <summary>
    /// 失败终结（带错误码）：界面按 Code 查语言包渲染，reason 仅供日志/调试。
    /// </summary>
    protected void Fail(string code, string reason, params string[] args)
    {
        Code = code;
        ErrorArgs = args;
        SetTerminal(OperationState.Failed, reason);
    }

    /// <summary>
    /// 等待操作到终态（阻塞，终态事件唤醒不空转）。
    /// 用于 RPC/工具等需要结果的同步调用方；模块扫描线程不要用（等待发生在状态机里）。
    /// </summary>
    public bool WaitReply(int timeoutMilliseconds)
    {
        return _terminal.Wait(timeoutMilliseconds);
    }

    private readonly ManualResetEventSlim _terminal = new(false);

    private void SetTerminal(OperationState state, string reason)
    {
        if (IsTerminal)
        {
            return;
        }

        _state = state;
        Reason = reason ?? string.Empty;
        Watch.Stop();
        _terminal.Set();
    }
}
