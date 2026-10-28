using xyz.Elements.Attributes;

namespace xyz.Elements.Interfaces;

/// <summary>
/// 整机给别的元件用的那一面：元件层看不到整机模块，要整机的东西经它取
/// </summary>
[Ioc]
public interface ISystemAccess
{
    /// <summary>
    /// 界面语言（machine.xml System 节点的 Language，如 zh-CN、en-US）
    /// </summary>
    string Language { get; }
}
