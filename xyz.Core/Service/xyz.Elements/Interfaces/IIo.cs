using xyz.Elements.Models;
using xyz.Shared.Dtos;

namespace xyz.Elements.Interfaces;

/// <summary>
/// IO 点表
/// </summary>
public interface IIo
{
    /// <summary>
    /// DI 点表
    /// </summary>
    IoPointTable Di { get; }

    /// <summary>
    /// DO 点表
    /// </summary>
    IoPointTable Do { get; }

    /// <summary>
    /// AI 点表
    /// </summary>
    IoPointTable Ai { get; }

    /// <summary>
    /// AO 点表
    /// </summary>
    IoPointTable Ao { get; }

    /// <summary>
    /// 读一个 DI，读不到返回 false
    /// </summary>
    bool TryReadDi(int index, out bool on);

    /// <summary>
    /// 读一个 DO 的回读，读不到返回 false
    /// </summary>
    bool TryReadDo(int index, out bool on);

    /// <summary>
    /// 写一个 DO，没写成带原因
    /// </summary>
    HandleResult WriteDo(int index, bool on);

    /// <summary>
    /// 读一个 AI（换算成工程值），读不到返回 false
    /// </summary>
    bool TryReadAi(int index, out double value);

    /// <summary>
    /// 读一个 AO 的回读（工程值），读不到返回 false
    /// </summary>
    bool TryReadAo(int index, out double value);

    /// <summary>
    /// 按工程值写一个 AO，标定过的点超出工程量范围不写，没写成带原因
    /// </summary>
    HandleResult WriteAo(int index, double engineering);

    /// <summary>
    /// 挂上虚拟输入点的登记表（装配时）
    /// </summary>
    internal void AttachVirtualInputs(VirtualInputRegistry registry);
}
