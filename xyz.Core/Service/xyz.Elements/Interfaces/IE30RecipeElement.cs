using xyz.Elements.Attributes;

namespace xyz.Elements.Interfaces;

/// <summary>
/// EAP 配方管理（S7）
/// </summary>
[Ioc]
public interface IE30RecipeElement
{
    /// <summary>
    /// 在线远程时锁住本地编辑
    /// </summary>
    bool IsLocalEditLocked { get; }

    /// <summary>
    /// 接到链路、GEM 和两个配方库上（没装的库传 null）
    /// </summary>
    void Attach(IHsms link, IE30Element gem, ISequenceLibrary? sequences, IRecipeLibrary? processRecipes);

    /// <summary>
    /// 从设备上摘下来（宿主退出）
    /// </summary>
    void Detach();
}
