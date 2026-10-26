using xyz.Components.Components;
using xyz.Shared.Dtos;

namespace xyz.Modules;

/// <summary>
/// 整机：管全部模块，自己不算模块
/// </summary>
public interface ISystemModule
{
    string Name { get; }

    int State { get; }

    string Language { get; }

    HandleResult<ModuleRoutine> InitAll();

    HandleResult Auto();

    HandleResult Manual();

    HandleResult<ModuleRoutine> Abort();

    HandleResult<ModuleRoutine> ReturnAll();
}
