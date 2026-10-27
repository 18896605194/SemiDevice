namespace xyz.Components.Interfaces;

/// <summary>
/// 整机给别的组件用的那一面：组件层看不到整机模块，要整机的东西经它取
/// </summary>
public interface ISystemAccess
{
    /// <summary>
    /// 界面语言（machine.xml System 节点的 Language，如 zh-CN、en-US）
    /// </summary>
    string Language { get; }
}
