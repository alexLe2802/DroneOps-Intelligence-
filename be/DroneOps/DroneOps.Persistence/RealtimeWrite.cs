using Microsoft.EntityFrameworkCore;
using DroneOps.Persistence.Data;

namespace DroneOps.Persistence;

// One aircraft deployment: serialize detector, timer and operator writes together.
// The PostgreSQL transaction lock works across API processes and is released on rollback.
public static class RealtimeWrite
{
    private static readonly SemaphoreSlim FixtureLock = new(1, 1);

    public static async Task<T> RunAsync<T>(AppDbContext db, Func<Task<T>> work, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            await FixtureLock.WaitAsync(ct);
            try { return await work(); }
            finally { FixtureLock.Release(); }
        }
        if (db.Database.CurrentTransaction != null) return await work();
        T result = default!;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(490, 3)", ct);
            try
            {
                result = await work();
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                throw;
            }
        });
        return result;
    }
}
