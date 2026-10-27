using xyz.Elements.Attributes;
using xyz.Elements.Interfaces;

namespace xyz.Elements.Elements;

/// <summary>
/// 虚拟 IO：下面挂的虚拟 DO 按点号读写，只在内存里、不下发 PLC，开机都是断
/// </summary>
[Element(description: "虚拟 IO（下面挂虚拟 DO 子节点，按点号读写；只在内存里、不下发 PLC，开机都是断）")]
public class VirtualIoElement : ElementBase, IVirtualIo
{
    private readonly object _gate = new();

    /// <summary>
    /// 点号 → 现在是通还是断（开机 Bind 时照下面的虚拟 DO 建）
    /// </summary>
    private Dictionary<int, bool> _values = [];

    /// <summary>
    /// 开机照下面的虚拟 DO 建点表：点号重了开机就报
    /// </summary>
    public override void Bind()
    {
        var paths = new Dictionary<int, string>();
        foreach (var point in FindChildren<IVirtualDo>())
        {
            if (!paths.TryAdd(point.Index, point.FullPath))
            {
                throw new InvalidOperationException($"虚拟 DO{point.Index} 重复：{paths[point.Index]}、{point.FullPath}");
            }
        }

        var values = new Dictionary<int, bool>();
        foreach (int index in paths.Keys)
        {
            values[index] = false;
        }

        lock (_gate)
        {
            _values = values;
        }
    }

    public bool TryReadDo(int index, out bool on)
    {
        lock (_gate)
        {
            return _values.TryGetValue(index, out on);
        }
    }

    public bool WriteDo(int index, bool on)
    {
        lock (_gate)
        {
            if (!_values.ContainsKey(index))
            {
                return false;
            }

            _values[index] = on;
            return true;
        }
    }

    public bool ToggleDo(int index)
    {
        lock (_gate)
        {
            if (!_values.TryGetValue(index, out bool on))
            {
                return false;
            }

            _values[index] = !on;
            return true;
        }
    }
}
