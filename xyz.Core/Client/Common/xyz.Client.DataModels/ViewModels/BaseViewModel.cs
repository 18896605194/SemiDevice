using CommunityToolkit.Mvvm.ComponentModel;

namespace xyz.Client.DataModels.ViewModels;

/// <summary>
/// 客户端 ViewModel 的统一基类。
/// </summary>
public abstract class BaseViewModel : ObservableObject
{
    /// <summary>
    /// 在属性、集合和命令完成初始化后加载页面数据。
    /// </summary>
    public virtual void Init()
    {
    }
}
