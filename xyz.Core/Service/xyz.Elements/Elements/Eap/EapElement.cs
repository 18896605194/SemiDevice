using xyz.Common.Log;
using xyz.Elements.Attributes;
using xyz.Elements.Interfaces;
using xyz.Tools;

namespace xyz.Elements.Elements;

/// <summary>
/// EAP（SECS/GEM，machine.xml 的 Eap 节点）：链路 Hsms、上报派发 Notifier，加上每个 SEMI 标准一个元件（E30、E39、E87、E90、E40、E94）
/// 和 E30 里的配方管理 Recipe、Trace 数据采集 Trace，都是它的子节点。
/// 开机分两步：模块开始扫描之前 <see cref="Bind"/>，按顺序把各标准接到链路和设备上（只挂上报口、登记处理方）；设备侧都起来以后 <see cref="Open"/> 打开链路——
/// Host 一连进来就可能发报文，处理方和设备都得先在。链路没启用（Hsms 的 IsEnable=False）时什么都不接，设备侧的上报口保持 null，照常跑。
/// 子节点哪个没配就不接哪个（Hsms、Notifier、E30 是必须的；Recipe / Trace / E87 / E90 / E40 / E94 按机台需要配，E94 建 CJ 要 E39）。
/// </summary>
[Element(description: "EAP（SECS/GEM）：链路加各 SEMI 标准，开机先接到设备上、设备起来后再开链路")]
public class EapElement : ElementBase, IEapElement
{
    /// <summary>各标准接到设备上了（链路启用、必需的节点都在）；链路由 <see cref="Open"/> 另开。</summary>
    public bool IsBound { get; private set; }

    /// <summary>
    /// 开机（开始扫描之前）：把各标准接到 LoadPort、Job 管理、两个配方库上
    /// </summary>
    public override void Bind()
    {
        Bind(IocHelper.GetServices<ILoadPortAccess>(), IocHelper.GetService<IJobManager>(), IocHelper.GetService<ISequenceLibrary>(), IocHelper.GetService<IRecipeLibrary>());
    }

    /// <summary>
    /// 设备侧都开始扫描以后开链路：Host 一连进来就可能发报文，处理方和设备都得先在
    /// </summary>
    public override void OnStarted()
    {
        Open();
    }

    /// <summary>
    /// 接设备（宿主在模块开始扫描之前调一次）：E30 先接（别的标准报事件、判控制状态都经它），再 E39（对象服务，别的标准往里登记对象），
    /// 再配方管理（两个配方库）、Trace、E90（晶圆账）、E87（LoadPort，槽图认定后通知 E90 建片对象）、E40 / E94（Job 管理）。不开链路。
    /// 要赶在扫描之前：开机时端口上已有的载具第一拍扫描就判到、自动读码，上报口没挂上的话到达和读码结果就丢了，
    /// E87 不知道这个载具，Host 没法认定，这一盒一直取不了片。这时报的事 Host 收不收得到由 E30 管（没在线不报、没通讯按缓存设置）。
    /// 配方库没装的传 null，那个库就不给 Host 管。
    /// </summary>
    public void Bind(IReadOnlyList<ILoadPortAccess> ports, IJobManager? jobs, ISequenceLibrary? sequences, IRecipeLibrary? processRecipes)
    {
        var link = FindChild<IHsms>();
        if (link is null)
        {
            LogHelper.Warn(this, "Eap 下没配 Hsms 节点：EAP 不接");
            return;
        }

        if (!link.IsEnable)
        {
            LogHelper.Info(this, "EAP 链路未启用（Hsms 的 IsEnable=False）：各标准不接到设备上，设备照常跑");
            return;
        }

        if (FindChild<IEapNotifierElement>() is null)
        {
            LogHelper.Error(this, "Eap 下没配 Notifier 节点：设备侧报给 EAP 的事没有派发线程，EAP 不接");
            return;
        }

        var gem = FindChild<IE30Element>();
        if (gem is null)
        {
            LogHelper.Error(this, "Eap 下没配 E30 节点：GEM 是 EAP 的底子，没有它 EAP 不接");
            return;
        }

        gem.Attach(link);
        var objects = FindChild<IE39Element>();
        objects?.Attach(link, gem);

        var recipes = FindChild<IE30RecipeElement>();
        recipes?.Attach(link, gem, sequences, processRecipes);

        var traces = FindChild<IE30TraceElement>();
        traces?.Attach(link, gem);

        var substrates = FindChild<IE90Element>();
        var carriers = FindChild<IE87Element>();
        var ledger = IocHelper.GetService<ILedgerManager>();
        if (substrates is not null)
        {
            if (ledger is null)
            {
                LogHelper.Warn(this, "没有晶圆账（LedgerManager）：E90 不接");
                substrates = null;
            }
            else
            {
                substrates.Attach(objects, ledger, waitForCarrier: carriers is not null);
            }
        }

        carriers?.Attach(link, gem, ports, substrates);

        var processJobs = FindChild<IE40Element>();
        var controlJobs = FindChild<IE94Element>();
        if (jobs is null)
        {
            if (processJobs is not null || controlJobs is not null)
            {
                LogHelper.Warn(this, "没有 Job 管理：E40 / E94 不接，Host 建不了 Job");
            }
        }
        else
        {
            processJobs?.Attach(link, gem, objects, jobs);
            controlJobs?.Attach(link, gem, objects, jobs);
        }

        IsBound = true;
        LogHelper.Info(this, "EAP 接到设备上了：" + string.Join("、", Children.Select(child => child.Name)));
    }

    /// <summary>打开链路（宿主在设备侧都起来以后调一次）：Host 一连进来就可能发报文，得等处理方和设备都在。没接上（<see cref="Bind"/> 没接成）的不开。</summary>
    public void Open()
    {
        if (!IsBound)
        {
            return;
        }

        FindChild<IHsms>()?.Open();
        LogHelper.Info(this, "EAP 链路打开");
    }

    /// <summary>收：先断链路（发 Separate），再把各标准从设备上摘下来。宿主退出时调。</summary>
    public void Close()
    {
        FindChild<IHsms>()?.Close();
        if (!IsBound)
        {
            return;
        }

        FindChild<IE94Element>()?.Detach();
        FindChild<IE40Element>()?.Detach();
        FindChild<IE87Element>()?.Detach();
        FindChild<IE90Element>()?.Detach();
        FindChild<IE30TraceElement>()?.Detach();
        FindChild<IE30RecipeElement>()?.Detach();
        FindChild<IE30Element>()?.Detach();
        IsBound = false;
    }

    /// <summary>
    /// 宿主退出：先断链路（发 Separate），再把各标准从设备上摘下来
    /// </summary>
    public override void OnStopping()
    {
        Close();
    }
}
