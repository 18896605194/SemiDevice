using Mapster;
using ProtoBuf.Grpc;
using SqlSugar;
using System.Text.Json;
using xyz.Database.Auth;
using xyz.Database.DbProvider;
using xyz.Shared.Dtos;
using xyz.Shared.Services;

namespace xyz.Service.UserManger;

/// <summary>
/// 菜单 gRPC 服务实现，菜单数据来自数据库。
/// </summary>
public class MenuService : RepositoryBase<MenuEntity>, IMenuService
{
    static MenuService()
    {
        XyzDb.InitTables<MenuEntity>();
        EnsureSeedData();
    }

    public async Task<RpcResponse> GetMenusAsync(RpcRequest request, CallContext context = default)
    {
        var entities = await GetAllEnabledAsync();
        var menus = entities
            .OrderBy(menu => menu.Sort)
            .Select(menu => menu.Adapt<MenuDto>())
            .ToList();

        return RpcResponse.Ok(JsonSerializer.Serialize(menus));
    }

    public async Task<RpcResponse> CreateMenuAsync(RpcRequest request, CallContext context = default)
    {
        var entity = new MenuEntity
        {
            Name = GetParameter(request, "Name"),
            Code = GetParameter(request, "Code"),
            ParentId = GetParameterAsLongOrNull(request, "ParentId"),
            Sort = GetParameterAsInt(request, "Sort", 1),
            IsEnabled = GetParameterAsBool(request, "IsEnabled", true)
        };

        entity.Id = await InsertAsync(entity);

        return RpcResponse.Ok(JsonSerializer.Serialize(entity.Adapt<MenuDto>()));
    }

    public async Task<RpcResponse> SaveMenuAsync(RpcRequest request, CallContext context = default)
    {
        var idText = GetParameter(request, "Id");
        if (!long.TryParse(idText, out var id))
        {
            return RpcResponse.Fail("无效的菜单 Id");
        }

        var entity = await GetByIdAsync(id);
        if (entity == null)
        {
            return RpcResponse.Fail("菜单不存在");
        }

        entity.Name = GetParameter(request, "Name");
        entity.Code = GetParameter(request, "Code");
        entity.ParentId = GetParameterAsLongOrNull(request, "ParentId");
        entity.Sort = GetParameterAsInt(request, "Sort", entity.Sort);
        entity.IsEnabled = GetParameterAsBool(request, "IsEnabled", entity.IsEnabled);

        await UpdateAsync(entity);

        return RpcResponse.Ok(JsonSerializer.Serialize(entity.Adapt<MenuDto>()));
    }

    public async Task<RpcResponse> DeleteMenuAsync(RpcRequest request, CallContext context = default)
    {
        var idText = GetParameter(request, "Id");
        if (!long.TryParse(idText, out var id))
        {
            return RpcResponse.Fail("无效的菜单 Id");
        }

        await DeleteAsync(menu => menu.Id == id);

        return RpcResponse.Ok("{}");
    }

    private static string GetParameter(RpcRequest request, string key)
    {
        return request.Parameters.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static long? GetParameterAsLongOrNull(RpcRequest request, string key)
    {
        var text = GetParameter(request, key);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return long.TryParse(text, out var value) ? value : null;
    }

    private static int GetParameterAsInt(RpcRequest request, string key, int defaultValue)
    {
        var text = GetParameter(request, key);
        return int.TryParse(text, out var value) ? value : defaultValue;
    }

    private static bool GetParameterAsBool(RpcRequest request, string key, bool defaultValue)
    {
        var text = GetParameter(request, key);
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        return bool.TryParse(text, out var value) ? value : defaultValue;
    }

    private static void EnsureSeedData()
    {
        using var db = XyzDb.Create();
        var hasMain = db.Queryable<MenuEntity>()
            .Where(menu => menu.Code == "Main")
            .Count() > 0;

        if (!hasMain)
        {
            // 兼容旧数据：没有固定一级菜单时，清理后重新写入固定种子。
            if (db.Queryable<MenuEntity>().Count() > 0)
            {
                db.Deleteable<MenuEntity>().ExecuteCommand();
            }

            InsertMenu(db, "Main", "Main", null, 1);
            InsertMenu(db, "Manual", "Manual", null, 2);
            InsertMenu(db, "Recipe", "Recipe", null, 3);
            InsertMenu(db, "Alarm", "Alarm", null, 4);
            InsertMenu(db, "DataCenter", "DataCenter", null, 5);
            InsertMenu(db, "Setting", "Setting", null, 6);
            InsertMenu(db, "Io", "Io", null, 7);

            EnsureSettingChildren(db);
            return;
        }

        // 已有数据时，只补齐缺失的固定一级菜单。
        EnsureTopLevel(db, "Main", "Main", 1);
        EnsureTopLevel(db, "Manual", "Manual", 2);
        EnsureTopLevel(db, "Recipe", "Recipe", 3);
        EnsureTopLevel(db, "Alarm", "Alarm", 4);
        EnsureTopLevel(db, "DataCenter", "DataCenter", 5);
        EnsureTopLevel(db, "Setting", "Setting", 6);
        EnsureTopLevel(db, "Io", "Io", 7);

        MigrateMenuCode(db, "Setting.UserManagement", "Setting.User");
        MigrateMenuCode(db, "Setting.RoleManagement", "Setting.Role");
        MigrateMenuCode(db, "Setting.MenuManagement", "Setting.Menu");

        EnsureSettingChildren(db);
    }

    private static void EnsureTopLevel(SqlSugarClient db, string name, string code, int sort)
    {
        var exists = db.Queryable<MenuEntity>()
            .Where(menu => menu.Code == code)
            .Count() > 0;

        if (!exists)
        {
            InsertMenu(db, name, code, null, sort);
        }
    }

    private static void MigrateMenuCode(SqlSugarClient db, string oldCode, string newCode)
    {
        var oldItems = db.Queryable<MenuEntity>()
            .Where(menu => menu.Code == oldCode)
            .ToList();

        if (oldItems.Count == 0)
        {
            return;
        }

        var newExists = db.Queryable<MenuEntity>()
            .Where(menu => menu.Code == newCode)
            .Count() > 0;

        if (!newExists)
        {
            foreach (var item in oldItems)
            {
                item.Code = newCode;
                db.Updateable(item).ExecuteCommand();
            }
        }
        else
        {
            db.Deleteable<MenuEntity>()
                .Where(menu => menu.Code == oldCode)
                .ExecuteCommand();
        }
    }

    private static void EnsureSettingChildren(SqlSugarClient db)
    {
        var setting = db.Queryable<MenuEntity>()
            .First(menu => menu.Code == "Setting");

        if (setting == null)
        {
            return;
        }

        EnsureChild(db, setting.Id, "用户管理", "Setting.User", 4);
        EnsureChild(db, setting.Id, "角色管理", "Setting.Role", 5);
        EnsureChild(db, setting.Id, "菜单管理", "Setting.Menu", 6);
    }

    private static void EnsureChild(
        SqlSugarClient db,
        long parentId,
        string name,
        string code,
        int sort)
    {
        var exists = db.Queryable<MenuEntity>()
            .Where(menu => menu.Code == code)
            .Count() > 0;

        if (!exists)
        {
            InsertMenu(db, name, code, parentId, sort);
        }
    }

    private static MenuEntity InsertMenu(
        SqlSugarClient db,
        string name,
        string code,
        long? parentId,
        int sort)
    {
        var entity = new MenuEntity
        {
            Name = name,
            Code = code,
            ParentId = parentId,
            Sort = sort,
            IsEnabled = true
        };

        entity.Id = db.Insertable(entity).ExecuteReturnIdentity();
        return entity;
    }
}
