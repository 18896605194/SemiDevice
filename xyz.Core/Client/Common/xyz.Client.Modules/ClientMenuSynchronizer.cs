using System.Globalization;
using xyz.Client.Common.Log;
using xyz.Client.Common.Rpc;
using xyz.Shared.Dtos;
using xyz.Shared.Rpc;
using xyz.Shared.Services;

namespace xyz.Client.Modules;

/// <summary>
/// 机型菜单同步：把机型模块声明的菜单行补齐到后端菜单表（已存在则不动）。
/// 后端不在线时只记日志，不阻断客户端启动。
/// </summary>
public static class ClientMenuSynchronizer
{
    /// <summary>
    /// 在 MainViewModel 读取菜单之前调用，保证机型菜单本次启动就可见。
    /// </summary>
    public static void Ensure(IReadOnlyList<IClientModule> modules)
    {
        var menus = modules
            .OfType<IClientMenuProvider>()
            .SelectMany(provider => provider.Menus)
            .ToList();

        if (menus.Count == 0)
        {
            return;
        }

        try
        {
            var service = GrpcClientFactory.Create<IMenuService>();
            var existing = service.GetMenusAsync(new RpcRequest())
                .GetAwaiter()
                .GetResult()
                .DeserializeData<List<MenuDto>>();

            var byCode = existing.ToDictionary(menu => menu.Code, StringComparer.OrdinalIgnoreCase);

            foreach (var menu in menus)
            {
                if (byCode.ContainsKey(menu.Code))
                {
                    continue;
                }

                if (!byCode.TryGetValue(menu.ParentCode, out var parent))
                {
                    ClientLog.Warn("Client", $"机型菜单 {menu.Code} 的父菜单 {menu.ParentCode} 不存在，已跳过。");
                    continue;
                }

                var request = new RpcRequest();
                request.Parameters["Name"] = menu.Name;
                request.Parameters["Code"] = menu.Code;
                request.Parameters["ParentId"] = parent.Id.ToString(CultureInfo.InvariantCulture);
                request.Parameters["Sort"] = menu.Sort.ToString(CultureInfo.InvariantCulture);
                request.Parameters["IsEnabled"] = bool.TrueString;

                var created = service.CreateMenuAsync(request)
                    .GetAwaiter()
                    .GetResult()
                    .DeserializeData<MenuDto>();

                byCode[menu.Code] = created;
            }
        }
        catch (Exception exception)
        {
            ClientLog.Error("Client", $"机型菜单同步失败：{exception.Message}");
        }
    }
}
