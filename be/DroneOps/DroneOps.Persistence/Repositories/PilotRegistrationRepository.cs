using DroneOps.Domain.Entities;
using DroneOps.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace DroneOps.Persistence.Repositories;

public class PilotRegistrationRepository : IPilotRegistrationRepository
{
    private readonly AppDbContext _context;

    public PilotRegistrationRepository(
        AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(
        PilotRegistration registration)
    {
        await _context.PilotRegistrations.AddAsync(registration);
    }

    public async Task<bool> EmailExistsAsync(
        string email)
    {
        return await _context.PilotRegistrations
            .AnyAsync(x => x.Email == email);
    }

    public async Task<(List<PilotRegistration> Items, int TotalCount)>
    GetAllAsync(
        int pageNumber,
        int pageSize,
        string? search,
        string? status)
    {
        var query = _context.PilotRegistrations.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                x.FullName.Contains(search) ||
                x.Email.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }


    public async Task<PilotRegistration?> GetByIdAsync(Guid id)
    {
        return await _context.PilotRegistrations
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}