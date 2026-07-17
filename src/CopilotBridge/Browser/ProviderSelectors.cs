using System.Reflection;
using System.Text.Json;

namespace CopilotBridge.Browser;

internal sealed record ProviderSelectors
{
    public required string AllowedHost { get; init; }
    public required string[] ModelSwitcher { get; init; }
    public required string[] Composer { get; init; }
    public required string[] SendButton { get; init; }
    public required string UserMessages { get; init; }
    public required string AssistantMessages { get; init; }
    public required string Busy { get; init; }
    public required string Opus { get; init; }
    public required string GptParent { get; init; }
    public required string Gpt56 { get; init; }
    public required string DeepThinkingChinese { get; init; }
    public required string DeepThinkingEnglish { get; init; }

    internal static ProviderSelectors Load()
    {
        const string resourceName = "CopilotBridge.Resources.m365-copilot-web.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName) ??
                           throw new InvalidOperationException($"Missing embedded resource {resourceName}.");
        return JsonSerializer.Deserialize<ProviderSelectors>(
                   stream,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
               throw new InvalidDataException("Provider selector resource is invalid.");
    }
}
