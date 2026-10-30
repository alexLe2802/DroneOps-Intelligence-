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
            RestrictionType=request.RestrictionType,
            VerticalRule=request.MinAltitudeMeters.HasValue||request.MaxAltitudeMeters.HasValue?"Band":request.VerticalRule,
            AltitudeReference=request.AltitudeReference,MinAltitudeMeters=request.MinAltitudeMeters,MaxAltitudeMeters=request.MaxAltitudeMeters,
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

        geofence.RestrictionType=request.RestrictionType;
        geofence.VerticalRule=request.MinAltitudeMeters.HasValue||request.MaxAltitudeMeters.HasValue?"Band":request.VerticalRule;
        geofence.AltitudeReference=request.AltitudeReference;geofence.MinAltitudeMeters=request.MinAltitudeMeters;geofence.MaxAltitudeMeters=request.MaxAltitudeMeters;
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
            RestrictionType=geofence.RestrictionType,
            VerticalRule=geofence.VerticalRule,
            AltitudeReference=geofence.AltitudeReference,MinAltitudeMeters=geofence.MinAltitudeMeters,MaxAltitudeMeters=geofence.MaxAltitudeMeters,Revision=geofence.Revision,
            Name = geofence.Name,
            Description = geofence.Description,
            Coordinates = coordinates,
            CreatedAt = geofence.CreatedAt
        };
    }
    public async Task<GeofenceCheckResult> CheckRestrictedAreaAsync(
       decimal latitude,
       decimal longitude,
       decimal? altitudeMeters = null,
       CancellationToken cancellationToken = default)
    {
        var geofences = await _geofenceRepository
            .GetAllAsync(cancellationToken);

        foreach (var geofence in geofences)
        {
            if (string.IsNullOrWhiteSpace(geofence.Coordinates))
                continue;

            List<List<decimal>>? polygon;

            try
            {
                polygon = JsonSerializer.Deserialize<List<List<decimal>>>(
                    geofence.Coordinates);
            }
            catch (JsonException)
            {
                continue;
            }

            if (polygon == null || polygon.Count < 3)
                continue;

            var insidePolygon = IsPointInsidePolygon(
                latitude,
                longitude,
                polygon);

            if (!insidePolygon)
                continue;

            // Nếu geofence không quy định độ cao
            // thì chỉ cần nằm trong polygon là bị chặn.
            if (geofence.MinAltitudeMeters == null &&
                geofence.MaxAltitudeMeters == null)
            {
                return CreateRestrictedResult(geofence);
            }

            // Caller không truyền altitude.
            // Không thể chứng minh điểm nằm ngoài vertical restriction,
            // nên coi polygon match là restricted.
            if (altitudeMeters == null)
            {
                return CreateRestrictedResult(geofence);
            }

            var aboveMin =
                geofence.MinAltitudeMeters == null ||
                altitudeMeters >= geofence.MinAltitudeMeters;

            var belowMax =
                geofence.MaxAltitudeMeters == null ||
                altitudeMeters <= geofence.MaxAltitudeMeters;

            if (aboveMin && belowMax)
            {
                return CreateRestrictedResult(geofence);
            }
        }

        return new GeofenceCheckResult
        {
            IsRestricted = false,
            Message = "Location is outside restricted geofences."
        };
    }

    private static GeofenceCheckResult CreateRestrictedResult(
        Domain.Entities.Geofence geofence)
    {
        return new GeofenceCheckResult
        {
            IsRestricted = true,
            GeofenceId = geofence.Id,
            GeofenceName = geofence.Name,
            Message = $"Location is inside restricted geofence '{geofence.Name}'."
        };
    }

    private static bool IsPointInsidePolygon(
     decimal latitude,
     decimal longitude,
     List<List<decimal>> polygon)
    {
        if (polygon is null || polygon.Count < 3)
        {
            return false;
        }

        var inside = false;
        var j = polygon.Count - 1;

        for (var i = 0; i < polygon.Count; i++)
        {
            if (polygon[i].Count < 2 ||
                polygon[j].Count < 2)
            {
                j = i;
                continue;
            }

            // Coordinates format: [longitude, latitude]
            var xi = polygon[i][0];
            var yi = polygon[i][1];

            var xj = polygon[j][0];
            var yj = polygon[j][1];

            var intersects =
                ((yi > latitude) != (yj > latitude)) &&
                (longitude <
                    (xj - xi) *
                    (latitude - yi) /
                    (yj - yi) +
                    xi);

            if (intersects)
            {
                inside = !inside;
            }

            j = i;
        }

        return inside;
    }
}
