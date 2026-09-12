namespace xyz.Modules;

/// <summary>
/// 空操作：构造即成功终态，用于无设备动作的默认实现（如 LoadPort 的准备阶段）。
/// 不挂载到模块扫描（终态 no-op），调用方直接读 IsSuccess。
/// </summary>
public sealed class NoOpOperation : ModuleOperation
{
    public NoOpOperation(string name) : base(name)
    {
        Complete();
    }

    protected override void OnScan()
    {
    }
}
