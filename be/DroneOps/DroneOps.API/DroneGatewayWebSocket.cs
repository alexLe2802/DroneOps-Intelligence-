using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DroneOps.Application.DTOs.Request.Telemetry;
using DroneOps.Application.Interfaces.Telemetry;
using FluentValidation;

namespace DroneOps.API;

public static class DroneGatewayWebSocket
{
    private const int MaxMessageBytes = 16 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            await context.Response.WriteAsJsonAsync(new { message = "Use the WebSocket protocol." });
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        GatewaySession? session = null;
        var windowStarted = DateTimeOffset.UtcNow;
        var messagesInWindow = 0;

        try
        {
            var hello = await ReceiveAsync(socket, context.RequestAborted);
            if (hello is null) return;

            var type = RequiredString(hello.RootElement, "type");
            if (type == "pair")
            {
                var request = RequiredPayload<PairDroneGateway>(hello.RootElement);
                await using var scope = context.RequestServices.CreateAsyncScope();
                var credential = await scope.ServiceProvider.GetRequiredService<IDroneConnectionService>()
                    .PairAsync(request, context.RequestAborted);
                session = new(credential.ConnectionId, credential.Token, credential.HardwareIdentity);
                await SendAsync(socket, new { type = "session", payload = credential }, context.RequestAborted);
            }
            else if (type == "resume")
            {
                var request = RequiredPayload<ResumeGateway>(hello.RootElement);
                session = new(request.ConnectionId, request.Token, request.HardwareIdentity);
                await using var scope = context.RequestServices.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IDroneConnectionService>()
                    .HeartbeatAsync(session.ConnectionId, session.Token, session.HardwareIdentity, context.RequestAborted);
                await SendAsync(socket, new { type = "ready" }, context.RequestAborted);
            }
            else
            {
                throw new ArgumentException("The first WebSocket message must be 'pair' or 'resume'.");
            }

            while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
            {
                var message = await ReceiveAsync(socket, context.RequestAborted);
                if (message is null) break;

                var now = DateTimeOffset.UtcNow;
                if (now - windowStarted >= TimeSpan.FromMinutes(1))
                {
                    windowStarted = now;
                    messagesInWindow = 0;
                }
                if (++messagesInWindow > 240)
                    throw new InvalidOperationException("Gateway message rate exceeded 240 messages per minute.");

                type = RequiredString(message.RootElement, "type");
                await using var scope = context.RequestServices.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IDroneConnectionService>();

                if (type == "heartbeat")
                {
                    await service.HeartbeatAsync(session.ConnectionId, session.Token, session.HardwareIdentity, context.RequestAborted);
                    await SendAsync(socket, new { type = "heartbeatAck", receivedAt = now }, context.RequestAborted);
                }
                else if (type == "telemetry")
                {
                    var key = RequiredString(message.RootElement, "idempotencyKey");
                    var sample = RequiredPayload<IngestTelemetrySampleRequest>(message.RootElement);
                    var validation = await scope.ServiceProvider.GetRequiredService<IValidator<IngestTelemetrySampleRequest>>()
                        .ValidateAsync(sample, context.RequestAborted);
                    if (!validation.IsValid)
                        throw new ArgumentException(string.Join("; ", validation.Errors.Select(x => x.ErrorMessage)));
                    var result = await service.SampleAsync(session.ConnectionId, session.Token, session.HardwareIdentity, key, sample, context.RequestAborted);
                    await SendAsync(socket, new { type = "telemetryAck", idempotencyKey = key, payload = result }, context.RequestAborted);
                }
                else
                {
                    throw new ArgumentException("Unsupported gateway WebSocket message type.");
                }
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (Exception error) when (error is ArgumentException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException or JsonException)
        {
            if (socket.State == WebSocketState.Open)
            {
                await SendAsync(socket, new { type = "error", message = error.Message }, CancellationToken.None);
                var status = error is UnauthorizedAccessException ? WebSocketCloseStatus.PolicyViolation : WebSocketCloseStatus.InvalidPayloadData;
                await socket.CloseAsync(status, "Gateway message rejected.", CancellationToken.None);
            }
        }
    }

    private static string RequiredString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new ArgumentException($"WebSocket message requires '{name}'.");

    private static T RequiredPayload<T>(JsonElement root)
        => root.TryGetProperty("payload", out var payload)
            ? payload.Deserialize<T>(Json) ?? throw new JsonException("WebSocket payload is empty.")
            : throw new ArgumentException("WebSocket message requires 'payload'.");

    private static async Task<JsonDocument?> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) throw new ArgumentException("Only JSON text messages are supported.");
            if (stream.Length + result.Count > MaxMessageBytes) throw new ArgumentException("Gateway message exceeds 16 KiB.");
            await stream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
            if (result.EndOfMessage) break;
        }
        stream.Position = 0;
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static Task SendAsync(WebSocket socket, object value, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json));
        return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private sealed record ResumeGateway(Guid ConnectionId, string Token, string HardwareIdentity);
    private sealed record GatewaySession(Guid ConnectionId, string Token, string HardwareIdentity);
}
