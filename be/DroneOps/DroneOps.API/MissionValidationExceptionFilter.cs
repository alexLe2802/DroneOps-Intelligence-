using DroneOps.Application.DTOs.Response.Missions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

public sealed class MissionValidationExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is MissionValidationException validation)
        {
            context.Result = new BadRequestObjectResult(new { message = validation.Message, validation = validation.Result });
            context.ExceptionHandled = true;
            return;
        }
        if (context.RouteData.Values["controller"]?.ToString() is not ("Missions" or "MissionEvidence" or "DroneConnections" or "DroneGateway" or "Uav")) return;
        int? status = context.Exception switch
        {
            System.Security.Authentication.AuthenticationException => 401,
            UnauthorizedAccessException => context.RouteData.Values["controller"]?.ToString() == "DroneGateway" ? 401 : 403,
            KeyNotFoundException => 404,
            InvalidOperationException => 409,
            ArgumentException => 400,
            _ => null
        };
        if (status is null) return;
        context.Result = new ObjectResult(new { message = context.Exception.Message }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
