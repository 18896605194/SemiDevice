using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using xyz.Client.Common.Ec;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Client.DataModels.ViewModels;
using xyz.Client.Presentation.Localization;
using xyz.Client.Setting.Models;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;

namespace xyz.Client.Setting.ViewModels;

/// <summary>
/// EC 设置页 ViewModel：左边组件树（层级、先后跟 sc.xml 的组件层级一样），右边列选中组件（含下级）的 EC 参数，一行一项。
/// 在那一行的"设定值"里填新值、点"设置"就改：后端按声明的格式、上下限查过才改，写回 ec.xml 并立即生效。
/// 参数和当前值都取自 ClientEc（连上后端拉全量，之后哪一项变了后端推哪一项），这一页自己不拉。
/// </summary>
public class EcViewModel : BaseViewModel
{
    #region Column

    /// <summary>
    /// 组件树：最上面是"全部"，下面照 sc.xml 的先后一个组件一个节点。
    /// </summary>
    public ObservableCollection<EcNodeModel> Nodes { get; } = [];

    /// <summary>
    /// 全部参数（按 sc.xml 的先后）；表格绑的是过滤后的 <see cref="ItemsView"/>。
    /// </summary>
    public ObservableCollection<EcItemModel> Items { get; } = [];

    /// <summary>
    /// 表格里列的：左边选中的组件和它下级的参数。
    /// </summary>
    public ICollectionView ItemsView { get; }

    #endregion

    #region Command

    /// <summary>
    /// 把那一行"设定值"里的新值改进去（参数：那一行）。
    /// </summary>
    public IAsyncRelayCommand<EcItemModel> SetCommand { get; }

    #endregion

    #region Service

    private readonly IEcService _service;

    /// <summary>
    /// 左边选中的节点；右边只列它（含下级）的参数。
    /// </summary>
    private EcNodeModel? _selectedNode;

    #endregion

    public EcViewModel()
    {
        _service = GrpcClientFactory.Create<IEcService>();
        ItemsView = new ListCollectionView(Items) { Filter = Matches };
        SetCommand = new AsyncRelayCommand<EcItemModel>(DoSet);
    }

    /// <summary>
    /// 跟着 EC 目录走：现在有就先列出来，后端晚起的话等目录拉回来再列。
    /// </summary>
    public override void Init()
    {
        ClientEc.Changed -= OnCatalogChanged;
        ClientEc.Changed += OnCatalogChanged;
        OnCatalogChanged();
    }

    /// <summary>
    /// EC 目录变了。参数还是那些（平时都是这样：只是某一项的值改了）就只刷当前值，选中的节点、正在填的设定值都不动；
    /// 参数有增减、定义变了（后端换了版本或 sc.xml 重启过）才整个重建。
    /// </summary>
    private void OnCatalogChanged()
    {
        var catalog = ClientEc.Items;
        if (catalog.Count != Items.Count || catalog.Where((dto, index) => !Items[index].HasSameDefinition(dto)).Any())
        {
            Rebuild(catalog);
            return;
        }

        for (int index = 0; index < catalog.Count; index++)
        {
            Items[index].Value = catalog[index].Value ?? string.Empty;
        }
    }

    /// <summary>
    /// 重建参数表和组件树：组件全路径按点号一级一级长出节点，先后就是后端给的先后；
    /// 原来选中、展开的节点还在的话照旧，不在了回到"全部"。
    /// </summary>
    private void Rebuild(IReadOnlyList<EcItemDto> catalog)
    {
        string selectedPath = _selectedNode?.Path ?? string.Empty;
        var expandedPaths = Flatten(Nodes)
            .Where(node => node.IsExpanded)
            .Select(node => node.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _selectedNode = null;
        Nodes.Clear();
        Items.Clear();
        foreach (var dto in catalog)
        {
            Items.Add(new EcItemModel(dto));
        }

        if (Items.Count == 0)
        {
            return;
        }

        var all = new EcNodeModel(L10n.Get("common.all"), string.Empty, OnNodeSelected) { Count = Items.Count };
        Nodes.Add(all);

        var nodes = new Dictionary<string, EcNodeModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
        {
            EcNodeModel? parent = null;
            string[] parts = item.Path.Split('.', StringSplitOptions.RemoveEmptyEntries);
            for (int level = 0; level < parts.Length; level++)
            {
                string path = string.Join('.', parts, 0, level + 1);
                if (!nodes.TryGetValue(path, out var node))
                {
                    node = new EcNodeModel(parts[level], path, OnNodeSelected) { IsExpanded = expandedPaths.Contains(path) };
                    nodes[path] = node;
                    (parent?.Children ?? Nodes).Add(node);
                }

                node.Count++;
                parent = node;
            }
        }

        (nodes.GetValueOrDefault(selectedPath) ?? all).IsSelected = true;
    }

    private static IEnumerable<EcNodeModel> Flatten(IEnumerable<EcNodeModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private void OnNodeSelected(EcNodeModel node)
    {
        _selectedNode = node;
        ItemsView.Refresh();
    }

    private bool Matches(object candidate)
    {
        return candidate is EcItemModel item && (_selectedNode is null || _selectedNode.Contains(item.Path));
    }

    /// <summary>
    /// 改一项：把"设定值"发给后端；改成了后端回改完的这一项，当前值跟着变，"设定值"清空。
    /// 没改成（超范围、写法不对、后端不在线）记到日志栏，"设定值"留着好接着改。
    /// </summary>
    private async Task DoSet(EcItemModel? item)
    {
        if (item is null || !item.CanSet)
        {
            return;
        }

        try
        {
            var response = await _service.SetValueAsync(new EcSetRequest { Key = item.Key, Value = item.PendingValue });
            if (!response.Success)
            {
                ClientLog.Error("EC", string.IsNullOrEmpty(response.Code)
                    ? response.Message
                    : L10n.Get(response.Code, response.Args));
                return;
            }

            // 后端也会推一条；这儿先按回包更新，事件流慢半拍界面也不会停在旧值上。
            ClientEc.Update(response.DeserializeData<EcItemDto>());
            item.PendingValue = string.Empty;
        }
        catch (Exception exception)
        {
            ClientLog.Error("EC", L10n.Get("setting.ec.set_failed", item.Key, exception.Message));
        }
    }
}
