using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using DroneOps.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

internal static class OperatorAllocationChecks
{
    public static async Task RunAsync()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"operator-allocation-{Guid.NewGuid()}").Options);
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var date = DateTime.UtcNow;
        db.Missions.AddRange(Enumerable.Range(0, 13).Select(index => new Mission
        {
            Id = Guid.NewGuid(), CreatedBy = index < 11 ? owner : other,
            Name = $"Allocation {index}", Status = index == 0 ? "Draft" : "Approved",
            CreatedAt = date.AddMinutes(index)
        }));
        await db.SaveChangesAsync();
        var repository = new MissionRepository(db);
        var first = await repository.GetAllAsync(null, null, null, null, 1, 10, createdBy: owner);
        var second = await repository.GetAllAsync(null, null, null, null, 2, 10, createdBy: owner);
        if (first.TotalCount != 11 || first.Items.Count != 10 || second.Items.Count != 1 ||
            first.Items.Concat(second.Items).Any(item => item.CreatedBy != owner) ||
            first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)).Any())
            throw new InvalidOperationException("Operator allocation filtering must precede counts and pagination.");
        var missing = await repository.GetAllAsync(null, null, null, null, 1, 10, createdBy: Guid.NewGuid());
        var approved = await repository.GetAllAsync(null, "Approved", null, null, 1, 20, createdBy: owner);
        if (missing.TotalCount != 0 || missing.Items.Count != 0 || approved.TotalCount != 10)
            throw new InvalidOperationException("Operator allocation empty/status filters must retain operator scope.");
        Console.WriteLine("PASS: Operator allocations retain owner scope across pagination, counts, status and empty results");
    }
}
