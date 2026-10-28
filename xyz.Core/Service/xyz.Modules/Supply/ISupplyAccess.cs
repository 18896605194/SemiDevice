using xyz.Elements.Attributes;

namespace xyz.Modules;

/// <summary>
/// 供液模块给别的模块用
/// </summary>
[Ioc]
public interface ISupplyAccess
{
    string Name { get; }

    bool IsReady { get; }
}
