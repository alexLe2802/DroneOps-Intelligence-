using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DroneOps.Persistence.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace DroneOps.API.AI;

[ApiController, Authorize, Route("api/ai/assessments")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AiAssessmentsController(NpgsqlDataSource dataSource, IAiAssessmentClient client,
    IOptions<AiSettings> options) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private SessionIdentity Current => (SessionIdentity)HttpContext.Items[typeof(SessionIdentity)]!;

    [HttpPost]
    public async Task<IActionResult> Create(CreateAiAssessmentRequest request, CancellationToken ct)
    {
        if (request.EndTime <= request.StartTime)
            return BadRequest(new { message = "Mission end time must be after its start time." });
        if (request.Incidents.Any(value => value.Length > 300))
            return BadRequest(new { message = "Incident summaries must not exceed 300 characters." });

        var settings = options.Value;
        var id = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow;
        var contextJson = JsonSerializer.Serialize(request, JsonOptions);
        var contextHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contextJson))).ToLowerInvariant();
        await InsertPendingAsync(id, request, settings, contextJson, contextHash, ct);
        try
        {
            var result = await client.AssessAsync(request, ct);
            var completed = DateTimeOffset.UtcNow;
            await CompleteAsync(id, result, completed, ct);
            return Ok(new AiAssessmentResponse(id, request.MissionRef, request.MissionVersion,
                settings.Provider, settings.Model, "Available", result, null, created, completed));
        }
        catch (AiProviderException e)
        {
            await FailAsync(id, e.Code, ct);
            return StatusCode(e.Code == "provider_rate_limited" ? 429 : 503,
                new { code = e.Code, message = "AI assessment is unavailable. Retry later or continue manual review.", assessmentId = id });
        }
    }

    [HttpGet("mission/{missionRef}")]
    public async Task<IActionResult> List(string missionRef, CancellationToken ct)
    {
        if (missionRef.Length is < 1 or > 80) return BadRequest(new { message = "Invalid mission reference." });
        await using var command = dataSource.CreateCommand("""
            SELECT id, mission_ref, mission_version, provider, model, status, result, error_code, created_at, completed_at
            FROM droneops.ai_assessments
            WHERE mission_ref = @mission AND (@manager OR requested_by = @account)
            ORDER BY created_at DESC LIMIT 20
            """);
        command.Parameters.AddWithValue("mission", missionRef);
        command.Parameters.AddWithValue("account", Current.Account.Id);
        command.Parameters.AddWithValue("manager", Current.Account.Role == AccountRoles.Manager);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var results = new List<AiAssessmentResponse>();
        while (await reader.ReadAsync(ct)) results.Add(Read(reader));
        return Ok(results);
    }

    private async Task InsertPendingAsync(Guid id, CreateAiAssessmentRequest request, AiSettings settings,
        string contextJson, string contextHash, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO droneops.ai_assessments
                (id, mission_ref, mission_version, requested_by, provider, model, context_hash, context_snapshot, status)
            VALUES (@id, @mission, @version, @requester, @provider, @model, @hash, @context, 'Pending')
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("mission", request.MissionRef.Trim());
        command.Parameters.AddWithValue("version", request.MissionVersion);
        command.Parameters.AddWithValue("requester", Current.Account.Id);
        command.Parameters.AddWithValue("provider", settings.Provider);
        command.Parameters.AddWithValue("model", settings.Model);
        command.Parameters.AddWithValue("hash", contextHash);
        command.Parameters.AddWithValue("context", NpgsqlDbType.Jsonb, contextJson);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task CompleteAsync(Guid id, AiAssessmentResult result, DateTimeOffset completed, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE droneops.ai_assessments SET status = 'Available', result = @result, completed_at = @completed WHERE id = @id
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("result", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(result, JsonOptions));
        command.Parameters.AddWithValue("completed", completed);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task FailAsync(Guid id, string code, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE droneops.ai_assessments SET status = 'Failed', error_code = @code, completed_at = now() WHERE id = @id
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("code", code);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static AiAssessmentResponse Read(NpgsqlDataReader reader)
    {
        var result = reader.IsDBNull(6) ? null : JsonSerializer.Deserialize<AiAssessmentResult>(reader.GetString(6), JsonOptions);
        return new(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4),
            reader.GetString(5), result, reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8), reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9));
    }
}
