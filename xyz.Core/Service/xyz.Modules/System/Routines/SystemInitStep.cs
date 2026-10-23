using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 全部初始化的一步：这一批模块一起发初始化（各走自己的 <see cref="IModule.InitModule"/>，超时由模块 EC 管），都收完尾算做完。
/// 有一个被拒（状态不允许、在忙，没写初始化的模块也算）或没做成就失败，已经在做的让它做完
/// </summary>
internal sealed class SystemInitStep : RoutineStep
{
    private readonly IReadOnlyList<IModule> _modules;
    private readonly List<(IModule Module, ModuleRoutine Routine)> _started = [];

    public SystemInitStep(string name, IReadOnlyList<IModule> modules) : base(name)
    {
        _modules = modules;
    }

    /// <summary>
    /// 还没收完尾的模块
    /// </summary>
    public override string Waiting
    {
        get
        {
            var pending = new List<string>();
            foreach (var (module, routine) in _started)
            {
                if (!routine.IsSettled)
                {
                    pending.Add(module.Name);
                }
            }

            return string.Join("、", pending);
        }
    }

    protected internal override void Run()
    {
        foreach (var module in _modules)
        {
            var routine = module.InitModule();
            if (routine is null)
            {
                Fail(ErrorCodes.SystemInitRejected, $"{module.Name} 初始化被拒", module.Name);
                return;
            }

            LogHelper.Info(module.Name, "全部初始化：开始初始化");
            _started.Add((module, routine));
        }
    }

    protected internal override bool Check()
    {
        foreach (var (_, routine) in _started)
        {
            if (!routine.IsSettled)
            {
                return false;
            }
        }

        foreach (var (module, routine) in _started)
        {
            if (!routine.IsSuccess)
            {
                Fail(ErrorCodes.SystemInitFailed, $"{module.Name} 初始化没做成（{routine.Reason}）", module.Name);
                return false;
            }
        }

        return true;
    }
}
