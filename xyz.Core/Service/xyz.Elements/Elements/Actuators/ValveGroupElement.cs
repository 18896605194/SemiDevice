using xyz.Elements.Attributes;
using xyz.Elements.Interfaces;

namespace xyz.Elements.Elements;

/// <summary>
/// 阀组：下面挂的 ValveElement 一起开、一起关
/// </summary>
[Element(description: "阀组（下面挂的 ValveElement 一起开、一起关）")]
public class ValveGroupElement : ElementBase, IValveGroup
{
    private IReadOnlyList<IValve> _valves = [];

    public override bool InitElement()
    {
        _valves = FindChildren<IValve>();
        return base.InitElement();
    }

    public bool Open()
    {
        // 一个没开成也接着开别的，用的地方看到 false 会整组关回去
        bool written = true;
        foreach (var valve in _valves)
        {
            if (!valve.Open())
            {
                written = false;
            }
        }

        return written;
    }

    public bool Close()
    {
        bool written = true;
        foreach (var valve in _valves)
        {
            if (!valve.Close())
            {
                written = false;
            }
        }

        return written;
    }
}
