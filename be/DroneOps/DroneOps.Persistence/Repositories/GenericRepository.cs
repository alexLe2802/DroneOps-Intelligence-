using System.Linq.Expressions;
using DroneOps.Application.Interfaces;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Repositories;

public class GenericRepository<T> : IGenericRepository<T>
    where T : class
{
    protected readonly AppDbContext DbContext;
    protected readonly DbSet<T> DbSet;

    public GenericRepository(AppDbContext dbContext)
    {
        DbContext = dbContext;
        DbSet = dbContext.Set<T>();
    }

    public async Task<T?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await DbSet.FindAsync(
            new object[] { id },
            cancellationToken);
    }

    public async Task<T?> FirstOrDefaultAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(predicate, cancellationToken);
    }

    public async Task<List<T>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await DbSet
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(
        T entity,
        CancellationToken cancellationToken = default)
    {
        await DbSet.AddAsync(entity, cancellationToken);
    }

    public void Update(T entity)
    {
        DbSet.Update(entity);
    }

    public void Remove(T entity)
    {
        DbSet.Remove(entity);
    }

    public async Task<bool> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return await DbContext.SaveChangesAsync(cancellationToken) > 0;
    }
}