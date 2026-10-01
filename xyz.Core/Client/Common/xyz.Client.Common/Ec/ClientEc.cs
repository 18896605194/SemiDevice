using xyz.Client.Common.Events;
using xyz.Client.Common.Rpc;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;
using xyz.Tools;

namespace xyz.Client.Common.Ec;

/// <summary>
/// 客户端的 EC 目录：连上后端时拉一次全部 EC（定义 + 当前值），之后哪一项的值变了后端推哪一项（EcItemDto）。
/// EC 设置页列的参数、通用输入框按 EcKey 取的范围都是这一份，不各自拉。按"组件全路径.参数名"查。
/// 全部在 UI 线程上动（事件流回调和连接状态本来就投递到 UI 线程），Changed 也在 UI 线程触发。
/// </summary>
public static class ClientEc
{
    private static Dictionary<string, EcItemDto> _items = new(StringComparer.OrdinalIgnoreCase);

    private static List<EcItemDto> _ordered = [];

    private static IDisposable? _subscription;

    /// <summary>
    /// 全部 EC，按后端给的先后——组件树的先后，也就是 sc.xml 的先后。
    /// </summary>
    public static IReadOnlyList<EcItemDto> Items => _ordered;

    /// <summary>
    /// 目录有变化：连上后端整表拉回来了，或者某一项的值改了。已经在界面上的控件据此重新取范围、刷新值。
    /// </summary>
    public static event Action? Changed;

    /// <summary>
    /// App 启动时（RemoteEventBus.Initialize 之后）调一次。
    /// </summary>
    public static void Initialize()
    {
        if (_subscription is not null)
        {
            return;
        }

        _subscription = EventBus.Register<EcItemDto>(EcItemDto.EventToken, Update);
        RemoteEventBus.ConnectionChanged += OnConnectionChanged;
        if (RemoteEventBus.IsConnected)
        {
            OnConnectionChanged(true);
        }
    }

    /// <summary>
    /// 按键查一项 EC，键是"组件全路径.参数名"（如 LoadPort1.LoadTimeout），忽略大小写；
    /// 还没拉到或没有这项返回 false。
    /// </summary>
    public static bool TryGet(string key, out EcItemDto item)
    {
        return _items.TryGetValue(key, out item!);
    }

    /// <summary>
    /// 一项的值变了（后端推来的，或本机刚改成功的回包）：换掉这一项并通知。
    /// 目录里没有这一项（整表还没拉到）就不管，等整表拉回来；值没变也不通知。
    /// </summary>
    public static void Update(EcItemDto item)
    {
        if (!_items.TryGetValue(item.Key, out var existing)
            || string.Equals(existing.Value, item.Value, StringComparison.Ordinal))
        {
            return;
        }

        _items[item.Key] = item;
        _ordered[_ordered.IndexOf(existing)] = item;
        Changed?.Invoke();
    }

    /// <summary>
    /// 连上后端时拉全量，整表替换（事件流断过也能对上账）。
    /// </summary>
    private static async void OnConnectionChanged(bool connected)
    {
        if (!connected)
        {
            return;
        }

        try
        {
            var response = await GrpcClientFactory.Create<IEcService>().GetDefinitionsAsync(new RpcRequest());
            Apply(response.DeserializeData<List<EcItemDto>>());
        }
        catch
        {
            // 后端不可用：断线重连时还会再拉；拉到之前各输入框按没配 EC 处理，EC 设置页是空的。
        }
    }

    /// <summary>
    /// 整表替换并通知。
    /// </summary>
    internal static void Apply(IEnumerable<EcItemDto> items)
    {
        var table = new Dictionary<string, EcItemDto>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<EcItemDto>();
        foreach (var item in items)
        {
            if (table.TryAdd(item.Key, item))
            {
                ordered.Add(item);
            }
        }

        _items = table;
        _ordered = ordered;
        Changed?.Invoke();
    }
}
