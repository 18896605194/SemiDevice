using xyz.Components.Components;
using xyz.Modules.Enums;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// Job 看设备的那一面：装配时绑模块表，每拍刷新一次搬运锁的快照；按名字找 LoadPort、站点、加工站点、机械手，查账、查锁。
/// 也是交给调度的设备视图（<see cref="IJobPlanEnvironment"/>）。只在 JobManager 的扫描线程上用。
/// </summary>
internal sealed class JobEnvironment : IJobPlanEnvironment
{
    private readonly IReadOnlyDictionary<string, BaseModule> _modules;
    private readonly IReadOnlyList<IRobot> _robots;

    public JobEnvironment(IEnumerable<BaseModule> modules)
    {
        var table = new Dictionary<string, BaseModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            table[module.Name] = module;
        }

        _modules = table;
        _robots = table.Values.OfType<IRobot>().ToList();
    }

    public TransferManager? Transfers => TransferManager.Current;

    public WaferManager? Ledger => WaferManager.Current;

    /// <summary>这一拍的搬运锁快照。</summary>
    public TransferView Locks { get; private set; } = TransferView.Empty;

    /// <summary>Auto 模式（自动派单开着）：Manual 下 Job 不派新动作。</summary>
    public bool IsAuto => Transfers?.IsAutoDispatch == true;

    public IReadOnlyList<IRobot> Robots => _robots;

    /// <summary>装了的 LoadPort（按 sc.xml 先后）。</summary>
    public IEnumerable<BaseLoadPortModule> LoadPorts => _modules.Values.OfType<BaseLoadPortModule>();

    /// <summary>每拍开头刷新一次锁快照。</summary>
    public void Refresh()
    {
        Locks = Transfers?.GetView() ?? TransferView.Empty;
    }

    public BaseModule? Module(string name)
    {
        return _modules.TryGetValue(name.Trim(), out var module) ? module : null;
    }

    public BaseLoadPortModule? LoadPort(string name)
    {
        return Module(name) as BaseLoadPortModule;
    }

    public ITransferStation? Station(string name)
    {
        return Module(name) as ITransferStation;
    }

    public IProcessStation? ProcessStation(string name)
    {
        return Module(name) as IProcessStation;
    }

    /// <summary>有机械手的站点表配了这个站点（机械手到得了）。</summary>
    public bool IsReachable(string station)
    {
        return _robots.Any(robot => robot.TryGetStation(station, out _));
    }

    /// <summary>
    /// LoadPort 上有能取片的载具：放着、Load 好了（在待命，或正在被机械手服务）。
    /// </summary>
    public bool IsCarrierReady(string loadPort)
    {
        var port = LoadPort(loadPort);
        if (port is null || !port.IsEnabled || !port.IsCarrierArrived)
        {
            return false;
        }

        int state = port.State;
        return state == LoadPortState.Loaded
            || (state >= TransferModuleState.PreTransfer && state <= TransferModuleState.TransferComplete);
    }

    public bool IsStationReady(string station)
    {
        var module = Module(station);
        if (module is null || !module.IsEnabled || module is not ITransferStation transfer)
        {
            return false;
        }

        // 腔体这类可选站点要在线才参与自动调度；LoadPort 是 Job 自己的载具，不看在线
        if (module is not BaseLoadPortModule && module.Mode != ModuleMode.Online)
        {
            return false;
        }

        if (!transfer.CanPrepare)
        {
            return false;
        }

        var current = module.CurrentOperation;
        return current is null || current.IsTerminal;
    }

    public int SlotCount(string station)
    {
        return Station(station)?.SlotCount ?? 0;
    }

    public bool IsSlotFree(string station, int slot)
    {
        var ledger = Ledger;
        return ledger is not null && ledger.Get(station, slot) is null && !Locks.IsSlotLocked(station, slot);
    }

    public string? RobotFor(string source, string target, IReadOnlySet<string> exclude)
    {
        var ledger = Ledger;
        if (ledger is null)
        {
            return null;
        }

        foreach (var robot in _robots)
        {
            if (exclude.Contains(robot.Name) || Locks.IsRobotBusy(robot.Name) || robot.State != ModuleState.Idle)
            {
                continue;
            }

            if (robot is BaseModule module && !module.IsEnabled)
            {
                continue;
            }

            if (!robot.TryGetStation(source, out var from) || !robot.TryGetStation(target, out var to))
            {
                continue;
            }

            int arms = ledger.GetSlots(robot.Name).Count;
            for (int arm = 1; arm <= arms; arm++)
            {
                if (from.AllowsArm(arm) && to.AllowsArm(arm)
                    && ledger.Get(robot.Name, arm) is null && !Locks.IsArmLocked(robot.Name, arm))
                {
                    return robot.Name;
                }
            }
        }

        return null;
    }

    public bool IsProcessStation(string station)
    {
        return ProcessStation(station) is not null;
    }

    public ProcessRejection? CheckProcess(string station, ProcessRequest request)
    {
        var process = ProcessStation(station);
        return process is null
            ? new ProcessRejection(ErrorCodes.JobWaitStation, [station])
            : process.CheckProcess(request);
    }
}
