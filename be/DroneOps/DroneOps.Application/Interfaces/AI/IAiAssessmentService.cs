using DroneOps.Application.DTOs.Request.AI;
using DroneOps.Application.DTOs.Response.AI;

namespace DroneOps.Application.Interfaces.AI;

public interface IAiAssessmentService
{
    Task<AiAssessmentResponse> CreateAsync(CreateAiAssessmentRequest request, Guid userId, bool isManager, CancellationToken cancellationToken);
    Task<List<AiAssessmentResponse>> GetByMissionAsync(string missionRef, Guid userId, bool isManager, CancellationToken cancellationToken);
}
