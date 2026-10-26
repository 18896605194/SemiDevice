using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 全部初始化的一步：这一批模块一起发初始化（各走自己的 <see cref="IModule.InitModule"/>，超时由模块 EC 管），都收完尾算做完。
/// 有一个被拒（照原样带模块给的原因）或没做成就失败，已经在做的让它做完
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
            var started = module.InitModule();
            var routine = started.Result;
            if (!started.IsSuccess || routine is null)
            {
                Fail(started.ErrorMessage, $"{module.Name} 初始化被拒", [.. started.Args]);
                return;
            }

            if (module is ILogSource source)
            {
                LogHelper.Info(source, "全部初始化：开始初始化");
            }
            else
            {
                LogHelper.Info(module.Name, "全部初始化：开始初始化");
            }
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
