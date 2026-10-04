using System.Text.Json;
using DroneOps.Application.DTOs.Request.Geofences;
using DroneOps.Application.DTOs.Response.Geofences;
using DroneOps.Application.Interfaces.Geofences;
using DroneOps.Domain.Entities;

namespace DroneOps.Application.Services;

public class GeofenceService : IGeofenceService
{
    private readonly IGeofenceRepository _geofenceRepository;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

    public GeofenceService(
        IGeofenceRepository geofenceRepository)
    {
        _geofenceRepository = geofenceRepository;
    }

    public async Task<List<GeofenceResponse>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var geofences =
            await _geofenceRepository.GetAllAsync(
                cancellationToken);

        return geofences
            .Select(MapToResponse)
            .ToList();
    }

    public async Task<GeofenceResponse> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var geofence =
            await _geofenceRepository.GetByIdAsync(
                id,
                cancellationToken);

        if (geofence == null)
        {
            throw new KeyNotFoundException(
                "Geofence was not found.");
        }

        return MapToResponse(geofence);
    }

    public async Task<GeofenceResponse> CreateAsync(
        CreateGeofenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var nameExists =
            await _geofenceRepository.NameExistsAsync(
                request.Name,
                null,
                cancellationToken);

        if (nameExists)
        {
            throw new InvalidOperationException(
                "Geofence name already exists.");
        }

        var geofence = new Geofence
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(
                request.Description)
                ? null
                : request.Description.Trim(),

            Coordinates = JsonSerializer.Serialize(
                request.Coordinates,
                JsonOptions),

            CreatedAt = DateTime.UtcNow
        };

        await _geofenceRepository.AddAsync(
            geofence,
            cancellationToken);

        await _geofenceRepository.SaveChangesAsync(
            cancellationToken);

        return MapToResponse(geofence);
    }

    public async Task<GeofenceResponse> UpdateAsync(
        Guid id,
        UpdateGeofenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var geofence =
            await _geofenceRepository.GetByIdAsync(
                id,
                cancellationToken);

        if (geofence == null)
        {
            throw new KeyNotFoundException(
                "Geofence was not found.");
        }

        var nameExists =
            await _geofenceRepository.NameExistsAsync(
                request.Name,
                id,
                cancellationToken);

        if (nameExists)
        {
            throw new InvalidOperationException(
                "Geofence name already exists.");
        }

        geofence.Name = request.Name.Trim();

        geofence.Description = string.IsNullOrWhiteSpace(
            request.Description)
            ? null
            : request.Description.Trim();

        geofence.Coordinates = JsonSerializer.Serialize(
            request.Coordinates,
            JsonOptions);

        _geofenceRepository.Update(geofence);

        await _geofenceRepository.SaveChangesAsync(
            cancellationToken);

        return MapToResponse(geofence);
    }

    public async Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var geofence =
            await _geofenceRepository.GetByIdAsync(
                id,
                cancellationToken);

        if (geofence == null)
        {
            throw new KeyNotFoundException(
                "Geofence was not found.");
        }

        _geofenceRepository.Delete(geofence);

        await _geofenceRepository.SaveChangesAsync(
            cancellationToken);
    }

    private static GeofenceResponse MapToResponse(
        Geofence geofence)
    {
        var coordinates =
            JsonSerializer.Deserialize<
                List<GeofenceCoordinateResponse>>(
                geofence.Coordinates,
                JsonOptions)
            ?? new List<GeofenceCoordinateResponse>();

        return new GeofenceResponse
        {
            Id = geofence.Id,
            Name = geofence.Name,
            Description = geofence.Description,
            Coordinates = coordinates,
            CreatedAt = geofence.CreatedAt
        };
    }
}