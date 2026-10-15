using xyz.Common.Log;
using xyz.Components.Components;
using xyz.Shared.Errors;

namespace xyz.Modules;

/// <summary>
/// 全部初始化：先把机械手回原点（手缩回来，别的站点动起来才不刮手），都做成了再一起初始化其余模块（LoadPort、腔体……）。
/// 每个模块走自己的 <see cref="BaseModule.InitModule"/>（各自的超时由模块 EC 管，这里不另设）；
/// 有一个被拒（状态不允许、在忙，没写初始化的模块也算）或没做成，整趟就算没做成，已经在做的让它做完。
/// </summary>
internal sealed class SystemInitOperation : ModuleOperation<SystemInitOperation.InitStep>
{
    internal enum InitStep
    {
        StartRobots,
        WaitRobots,
        StartOthers,
        WaitOthers,
    }

    private readonly IReadOnlyList<BaseModule> _robots;
    private readonly IReadOnlyList<BaseModule> _others;
    private readonly List<(BaseModule Module, ModuleOperation Operation)> _started = [];

    public SystemInitOperation(IReadOnlyList<BaseModule> modules) : base("Init", InitStep.StartRobots)
    {
        _robots = modules.Where(module => module is IRobot).ToList();
        _others = modules.Where(module => module is not IRobot).ToList();
    }

    protected override void OnScan()
    {
        switch (Step)
        {
            case InitStep.StartRobots:
                if (StartAll(_robots))
                {
                    SetStep(InitStep.WaitRobots);
                }

                break;
            case InitStep.WaitRobots:
                if (AllDone())
                {
                    SetStep(InitStep.StartOthers);
                }

                break;
            case InitStep.StartOthers:
                if (StartAll(_others))
                {
                    SetStep(InitStep.WaitOthers);
                }

                break;
            case InitStep.WaitOthers:
                if (AllDone())
                {
                    Complete();
                }

                break;
        }
    }

    /// <summary>这一批一起发初始化；有一个被拒就判失败（模块自己记了被拒的原因）。</summary>
    private bool StartAll(IReadOnlyList<BaseModule> modules)
    {
        _started.Clear();
        foreach (var module in modules)
        {
            var operation = module.InitModule();
            if (operation is null)
            {
                Fail(ErrorCodes.SystemInitRejected, $"{module.Name} 初始化被拒", module.Name);
                return false;
            }

            LogHelper.Info(module.Name, "全部初始化：开始初始化");
            _started.Add((module, operation));
        }

        return true;
    }

    /// <summary>这一批都收完尾了没有（模块落好状态才算）；有一个没做成就判失败。</summary>
    private bool AllDone()
    {
        foreach (var (_, operation) in _started)
        {
            if (!operation.IsSettled)
            {
                return false;
            }
        }

        foreach (var (module, operation) in _started)
        {
            if (!operation.IsSuccess)
            {
                Fail(ErrorCodes.SystemInitFailed, $"{module.Name} 初始化没做成（{operation.Reason}）", module.Name);
                return false;
            }
        }

        return true;
    }
}
