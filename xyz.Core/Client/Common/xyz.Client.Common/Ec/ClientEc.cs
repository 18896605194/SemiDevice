using xyz.Client.Common.Events;
using xyz.Client.Common.Rpc;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;

namespace xyz.Client.Common.Ec;

/// <summary>
/// 客户端的 EC 目录：连上后端时拉一次全部 EC 的定义（格式、上下限、单位……），按"组件全路径.参数名"查。
/// 通用输入框按 EcKey 取范围靠它；EC 的上下限是代码里 [VariableMark] 声明的，运行期不变，所以不跟事件流，重连时整表重拉。
/// 全部在 UI 线程上动（连接状态回调本来就投递到 UI 线程），Changed 也在 UI 线程触发。
/// </summary>
public static class ClientEc
{
    private static Dictionary<string, EcItemDto> _items = new(StringComparer.OrdinalIgnoreCase);

    private static bool _initialized;

    /// <summary>
    /// 目录换了（连上后端拉到了新的一份）。已经在界面上的控件据此重新取范围。
    /// </summary>
    public static event Action? Changed;

    /// <summary>
    /// App 启动时（RemoteEventBus.Initialize 之后）调一次。
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
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
            // 后端不可用：断线重连时还会再拉；拉到之前各输入框按没配 EC 处理。
        }
    }

    /// <summary>
    /// 整表替换并通知。
    /// </summary>
    internal static void Apply(IEnumerable<EcItemDto> items)
    {
        var table = new Dictionary<string, EcItemDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            table[item.Key] = item;
        }

        _items = table;
        Changed?.Invoke();
    }
}
