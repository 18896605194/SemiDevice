using xyz.Elements.Attributes;

namespace xyz.Modules;

/// <summary>
/// 不跟机械手交互的模块（供液 IPA、硫酸这类），跟 ITransferStation 平级
/// </summary>
[Ioc]
public interface ISupplyModule
{
    /// <summary>
    /// 模块名（如 IPA）
    /// </summary>
    string Name { get; }
}
