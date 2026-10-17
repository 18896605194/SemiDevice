using xyz.Components.Components;
using xyz.Components.Interfaces;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 整机
/// </summary>
public interface ISystemModule : IModule
{
    /// <summary>
    /// 界面语言（sc 的 System/Language）
    /// </summary>
    string Language { get; }

    /// <summary>
    /// 全部初始化
    /// </summary>
    HandleResult<ModuleOperation> InitAll();

    /// <summary>
    /// 切自动
    /// </summary>
    HandleResult Auto();

    /// <summary>
    /// 切手动
    /// </summary>
    HandleResult Manual();

    /// <summary>
    /// 整机停止能不能做，能是 null
    /// </summary>
    HandleResult? CheckAbort();

    /// <summary>
    /// 全部回片
    /// </summary>
    HandleResult<ModuleOperation> ReturnAll();
}
