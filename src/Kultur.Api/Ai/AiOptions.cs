using Anthropic;

namespace Kultur.Api.Ai;

public class AiOptions
{
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-opus-5-5";
    public string? BaseUrl { get; set; }           // tests point this at a fake

    public bool Configured => !string.IsNullOrWhiteSpace(ApiKey);

    public static AnthropicClient CreateClient(AiOptions o, HttpClient http) => o.BaseUrl is null
        ? new AnthropicClient { ApiKey = o.ApiKey?.Trim() ?? "", HttpClient = http }
        : new AnthropicClient { ApiKey = o.ApiKey?.Trim() ?? "", HttpClient = http, BaseUrl = o.BaseUrl };
}
