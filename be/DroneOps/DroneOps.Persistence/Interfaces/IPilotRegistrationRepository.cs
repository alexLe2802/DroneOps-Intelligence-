using DroneOps.Domain.Entities;

public interface IPilotRegistrationRepository
{
    Task AddAsync(PilotRegistration registration);

    Task<bool> EmailExistsAsync(string email);

    Task<(List<PilotRegistration> Items, int TotalCount)>
        GetAllAsync(
            int pageNumber,
            int pageSize,
            string? search,
            string? status);

    Task<PilotRegistration?> GetByIdAsync(Guid id);

    Task SaveChangesAsync();
}