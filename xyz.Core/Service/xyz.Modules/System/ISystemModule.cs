using xyz.Elements.Elements;
using xyz.Elements.Interfaces;
using xyz.Shared.Dtos;
using xyz.Elements.Attributes;

namespace xyz.Modules;

/// <summary>
/// 整机：管全部模块，自己不算模块
/// </summary>
[Ioc]
public interface ISystemModule : ISystemAccess
{
    string Name { get; }

    int State { get; }

    HandleResult<ModuleRoutine> InitAll();

    HandleResult Auto();

    HandleResult Manual();

    HandleResult<ModuleRoutine> Abort();

    HandleResult<ModuleRoutine> ReturnAll();
}
