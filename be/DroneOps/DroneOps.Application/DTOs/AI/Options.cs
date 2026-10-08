namespace DroneOps.Application.Options;

public sealed class OpenAIOptions
{
    public const string SectionName = "OpenAI";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string BaseUrl { get; set; }
        = "https://api.openai.com/v1/";
}