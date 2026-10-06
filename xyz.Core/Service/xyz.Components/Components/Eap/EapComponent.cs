using xyz.Common.Log;
using xyz.Components.Attributes;
using xyz.Components.Interfaces;

namespace xyz.Components.Components;

/// <summary>
/// EAP（SECS/GEM，sc.xml 的 Eap 节点）：链路 Hsms 加上每个 SEMI 标准一个组件（E30、E39、E87、E90、E40、E94），都是它的子节点。
/// 开机时设备侧（LoadPort、晶圆账、Job 管理）都起来以后，由它按顺序把各标准接到链路和设备上，最后才打开链路——
/// Host 一连进来就可能发报文，处理方得先在。链路没启用（Hsms 的 IsEnable=False）时什么都不接，设备侧的上报口保持 null，照常跑。
/// 子节点哪个没配就不接哪个（E30 和 Hsms 是必须的；E87 / E90 / E40 / E94 按机台需要配，E94 建 CJ 要 E39）。
/// </summary>
[Component(description: "EAP（SECS/GEM）：链路加各 SEMI 标准，开机按顺序接到设备上再开链路")]
public class EapComponent : ComponentBase
{
    /// <summary>当前 EAP 组件；sc.xml 没配 Eap 节点时为 null。</summary>
    public static EapComponent? Current { get; set; }

    public EapComponent()
    {
        Current = this;
    }

    /// <summary>接上了（链路启用、各标准接好、链路打开了）。</summary>
    public bool IsBound { get; private set; }

    /// <summary>
    /// 接设备、开链路（宿主在设备侧都起来以后调一次）：E30 先接（别的标准报事件、判控制状态都经它），再 E39（对象服务，别的标准往里登记对象），
    /// 再 E90（晶圆账）、E87（LoadPort，槽图认定后通知 E90 建片对象）、E40 / E94（Job 管理），最后打开链路。
    /// </summary>
    public void Bind(IReadOnlyList<ILoadPort> ports, IJobManager? jobs)
    {
        var link = FindChild<HsmsComponent>();
        if (link is null)
        {
            LogHelper.Warn(Name, "Eap 下没配 Hsms 节点：EAP 不接");
            return;
        }

        if (!link.IsEnable)
        {
            LogHelper.Info(Name, "EAP 链路未启用（Hsms 的 IsEnable=False）：各标准不接到设备上，设备照常跑");
            return;
        }

        var gem = FindChild<E30Component>();
        if (gem is null)
        {
            LogHelper.Error(Name, "Eap 下没配 E30 节点：GEM 是 EAP 的底子，没有它 EAP 不接");
            return;
        }

        gem.Attach(link);
        var objects = FindChild<E39Component>();
        objects?.Attach(link, gem);

        var substrates = FindChild<E90Component>();
        var carriers = FindChild<E87Component>();
        var ledger = WaferManager.Current;
        if (substrates is not null)
        {
            if (ledger is null)
            {
                LogHelper.Warn(Name, "没有晶圆账（WaferManager）：E90 不接");
                substrates = null;
            }
            else
            {
                substrates.Attach(gem, objects, ledger, waitForCarrier: carriers is not null);
            }
        }

        carriers?.Attach(link, gem, objects, ports, substrates is null ? null : substrates.MaterialArrived);

        var processJobs = FindChild<E40Component>();
        var controlJobs = FindChild<E94Component>();
        if (jobs is null)
        {
            if (processJobs is not null || controlJobs is not null)
            {
                LogHelper.Warn(Name, "没有 Job 管理：E40 / E94 不接，Host 建不了 Job");
            }
        }
        else
        {
            processJobs?.Attach(link, gem, objects, jobs, ports);
            controlJobs?.Attach(link, gem, objects, jobs);
        }

        link.Open();
        IsBound = true;
        LogHelper.Info(Name, "EAP 接好了：" + string.Join("、", Children.Select(child => child.Name)));
    }

    /// <summary>收：先断链路（发 Separate），再把各标准从设备上摘下来。宿主退出时调。</summary>
    public void Close()
    {
        FindChild<HsmsComponent>()?.Close();
        if (!IsBound)
        {
            return;
        }

        FindChild<E94Component>()?.Detach();
        FindChild<E40Component>()?.Detach();
        FindChild<E87Component>()?.Detach();
        FindChild<E90Component>()?.Detach();
        FindChild<E30Component>()?.Detach();
        IsBound = false;
    }
}
