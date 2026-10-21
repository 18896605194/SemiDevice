using System.Globalization;
using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Interfaces;
using xyz.Configs;
using xyz.Configs.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Components.Components;

/// <summary>
/// 晶圆尺寸组件
/// </summary>
[Component(description: "晶圆尺寸：8 寸 / 12 寸（主界面切换，写回 sc.xml）")]
public class WaferSizeComponent : ComponentBase, IWaferSize
{
    /// <summary>8 寸片（200 mm）。</summary>
    public const int Inch8 = 8;

    /// <summary>12 寸片（300 mm）。</summary>
    public const int Inch12 = 12;

    private readonly object _gate = new();

    /// <summary>
    /// 切换成功以后发（锁外），参数是新尺寸：配方页的字段上限跟着变，要通知界面重拉。
    /// </summary>
    public event Action<int>? Changed;

    #region SC

    [SCEditor("12", "System", "晶圆尺寸（寸）：8 或 12；主界面系统操作里切换（整机没初始化、机台里没片时才让切），切了马上生效并写回这里")]
    public int Size { get; set; } = Inch12;

    #endregion

    /// <summary>
    /// 现在做几寸片：用的地方把自己取到的晶圆尺寸组件传进来；没配晶圆尺寸组件（null）按 12 寸。
    /// </summary>
    public static int SizeOf(IWaferSize? component)
    {
        if (component is null)
        {
            return Inch12;
        }

        return component.Size;
    }

    /// <summary>
    /// 支持的尺寸：8 寸、12 寸。
    /// </summary>
    public static bool IsSupported(int size)
    {
        return size is Inch8 or Inch12;
    }

    /// <summary>
    /// 晶圆半径 mm，也就是配方晶圆坐标里中心的位置（边缘 = 0）：8 寸 100、12 寸 150。
    /// </summary>
    public static double RadiusOf(int size)
    {
        return size == Inch8 ? 100 : 150;
    }

    /// <summary>
    /// 尺寸只能是 8 或 12，写错了开机就报出来。
    /// </summary>
    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);
        if (!IsSupported(Size))
        {
            throw new InvalidOperationException($"{FullPath}：Size 只能是 {Inch8} 或 {Inch12}，现在是 {Size}");
        }
    }

    /// <summary>
    /// 切尺寸：先写回 sc.xml，写成了才换上、发 <see cref="Changed"/>（重启后还是这个尺寸）。跟现在一样直接成功。
    /// 能不能切（整机没初始化、机台里没片）由调用方先查，这里只管尺寸对不对、存不存得下。
    /// </summary>
    public HandleResult SetSize(int size)
    {
        string sizeText = size.ToString(CultureInfo.InvariantCulture);
        if (!IsSupported(size))
        {
            return HandleResult.Fail(ErrorCodes.WaferSizeInvalid, sizeText);
        }

        lock (_gate)
        {
            if (Size == size)
            {
                return HandleResult.Success();
            }

            try
            {
                SC.SaveValue(FullPath, nameof(Size), sizeText);
            }
            catch (Exception exception)
            {
                LogHelper.Error(Name, $"晶圆尺寸切到 {size} 寸没成：写回 sc.xml 失败，{exception.Message}");
                return HandleResult.Fail(ErrorCodes.WaferSizeSaveFailed, exception.Message);
            }

            Size = size;
        }

        LogHelper.Info(Name, $"晶圆尺寸切到 {size} 寸，已写回 sc.xml");
        Changed?.Invoke(size);
        return HandleResult.Success();
    }
}
