using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DroneOps.API.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class IntegrationAuthorizeAttribute : Attribute, IAsyncActionFilter
{
    private const string IntegrationAuthHeader = "Authorization";
    private const string ApiKeyHeader = "X-Integration-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var configuredKey = configuration["IntegrationSettings:ApiKey"];

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            context.Result = new ObjectResult(new { message = "Integration gateway is not configured." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
            return;
        }

        // Ưu tiên đọc từ Header Authorization, dự phòng qua X-Integration-Key
        string? providedKey = null;
        if (context.HttpContext.Request.Headers.TryGetValue(IntegrationAuthHeader, out var authHeaderValues))
        {
            var headerValue = authHeaderValues.ToString().Trim();
            // Xử lý cả trường hợp gửi "Bearer <key>" hoặc gửi trực tiếp "<key>"
            providedKey = headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? headerValue["Bearer ".Length..].Trim()
                : headerValue;
        }
        else if (context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeader, out var apiKeyValues))
        {
            providedKey = apiKeyValues.ToString().Trim();
        }

        // Kiểm tra khớp mã bí mật (Không log giá trị providedKey để đảm bảo an toàn bảo mật)
        if (string.IsNullOrEmpty(providedKey) || !string.Equals(configuredKey, providedKey, StringComparison.Ordinal))
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                message = "Invalid or missing integration credentials."
            });
            return;
        }

        await next();
    }
}