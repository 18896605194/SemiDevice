using System.Globalization;
using xyz.Common.Log;
using xyz.Elements.Attributes;
using xyz.Elements.Interfaces;
using xyz.Configs;
using xyz.Configs.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Errors;

namespace xyz.Elements.Elements;

/// <summary>
/// 晶圆尺寸元件
/// </summary>
[Element(description: "晶圆尺寸：8 寸 / 12 寸（主界面切换，写回 machine.xml）")]
public class WaferSizeElement : ElementBase, IWaferSize
{
    public const int Inch8 = 8;

    public const int Inch12 = 12;

    private readonly object _gate = new();

    public event Action<int>? Changed;

    #region Machine

    [Machine("12", "System", "晶圆尺寸（寸）：8 或 12；主界面系统操作里切换（整机没初始化、机台里没片时才让切），切了马上生效并写回这里")]
    public int Size { get; set; } = Inch12;

    #endregion

    protected internal override void OnSettingLoaded(ModuleConfig setting)
    {
        base.OnSettingLoaded(setting);
        if (!IsSupported(Size))
        {
            throw new InvalidOperationException($"{FullPath}：Size 只能是 {Inch8} 或 {Inch12}，现在是 {Size}");
        }
    }

    /// <summary>
    /// 支持的尺寸：8 寸、12 寸。
    /// </summary>
    public static bool IsSupported(int size)
    {
        return size is Inch8 or Inch12;
    }

    /// <summary>
    /// 切尺寸：先写回 machine.xml，写成了才换上、发 <see cref="Changed"/>（重启后还是这个尺寸）。跟现在的尺寸一样就直接成功。
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
                MachineLoader.SaveValue(FullPath, nameof(Size), sizeText);
            }
            catch (Exception exception)
            {
                LogHelper.Error(this, $"晶圆尺寸切到 {size} 寸没成：写回 machine.xml 失败，{exception.Message}");
                return HandleResult.Fail(ErrorCodes.WaferSizeSaveFailed, exception.Message);
            }

            Size = size;
        }

        LogHelper.Info(this, $"晶圆尺寸切到 {size} 寸，已写回 machine.xml");
        Changed?.Invoke(size);
        return HandleResult.Success();
    }
}
