using SqlSugar;
using System.Linq.Expressions;

namespace xyz.Database.DbProvider;

/// <summary>
/// 数据库操作基类。业务服务可继承本类复用通用 CRUD。
/// </summary>
public abstract class RepositoryBase<TEntity> where TEntity : BaseEntity, new()
{
    protected SqlSugarClient CreateDb()
    {
        return XyzDb.Create();
    }

    protected async Task<List<TEntity>> GetAllEnabledAsync()
    {
        using var db = CreateDb();
        return await db.Queryable<TEntity>()
            .Where(entity => entity.IsEnabled)
            .OrderBy(entity => entity.Id)
            .ToListAsync();
    }

    protected async Task<bool> AnyAsync(Expression<Func<TEntity, bool>> predicate)
    {
        using var db = CreateDb();
        return (await db.Queryable<TEntity>()
            .Where(predicate)
            .ToListAsync()).Count > 0;
    }

    protected async Task<long> InsertAsync(TEntity entity)
    {
        using var db = CreateDb();
        return await db.Insertable(entity).ExecuteReturnIdentityAsync();
    }

    protected async Task<TEntity?> GetByIdAsync(long id)
    {
        using var db = CreateDb();
        return await db.Queryable<TEntity>()
            .FirstAsync(entity => entity.Id == id);
    }

    protected async Task UpdateAsync(TEntity entity)
    {
        using var db = CreateDb();
        await db.Updateable(entity).ExecuteCommandAsync();
    }

    protected async Task DeleteAsync(Expression<Func<TEntity, bool>> predicate)
    {
        using var db = CreateDb();
        await db.Deleteable<TEntity>()
            .Where(predicate)
            .ExecuteCommandAsync();
    }
}
