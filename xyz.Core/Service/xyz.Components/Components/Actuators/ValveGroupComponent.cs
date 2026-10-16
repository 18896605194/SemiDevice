using xyz.Components.Attributes;
using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>
/// 阀组：下面挂的 ValveComponent 一起开、一起关
/// </summary>
[Component(description: "阀组（下面挂的 ValveComponent 一起开、一起关）")]
public class ValveGroupComponent : ComponentBase, IValveGroup
{
    private IReadOnlyList<IValve> _valves = [];

    public override bool InitComponent()
    {
        _valves = FindChildren<IValve>();
        return base.InitComponent();
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
