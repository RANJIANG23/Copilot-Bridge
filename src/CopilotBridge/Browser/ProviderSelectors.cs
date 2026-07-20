using System.Reflection;
using System.Text.Json;

namespace CopilotBridge.Browser;

internal sealed record ProviderSelectors
{
    public required string[] AllowedHosts { get; init; }
    public required string[] ModelSwitcher { get; init; }
    public required string[] Composer { get; init; }
    public required string[] SendButton { get; init; }
    public required string[] LoginRequired { get; init; }
    public required string UserMessages { get; init; }
    public required string AssistantMessages { get; init; }
    public required string Busy { get; init; }
    public required string Opus { get; init; }
    public required string GptParent { get; init; }
    public required string Gpt56 { get; init; }
    public required string DeepThinkingChinese { get; init; }
    public required string DeepThinkingEnglish { get; init; }

    internal string PreferredHost => AllowedHosts[0];

    internal bool IsAllowedHost(string host) =>
        AllowedHosts.Any(allowed => allowed.Equals(host, StringComparison.OrdinalIgnoreCase));

    internal bool IsAllowedChatUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !IsAllowedHost(uri.Host))
        {
            return false;
        }

        return uri.AbsolutePath.Equals("/chat", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.StartsWith("/chat/", StringComparison.OrdinalIgnoreCase);
    }

    internal bool IsAllowedConversationUrl(string value) =>
        IsAllowedChatUrl(value) &&
        new Uri(value).AbsolutePath.StartsWith("/chat/conversation/", StringComparison.OrdinalIgnoreCase);

    internal string NewChatUrlFor(string? relatedUrl)
    {
        var host = Uri.TryCreate(relatedUrl, UriKind.Absolute, out var uri) && IsAllowedHost(uri.Host)
            ? uri.Host
            : PreferredHost;
        return $"https://{host}/chat/";
    }

    internal static ProviderSelectors Load()
    {
        const string resourceName = "CopilotBridge.Resources.m365-copilot-web.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName) ??
                           throw new InvalidOperationException($"Missing embedded resource {resourceName}.");
        var selectors = JsonSerializer.Deserialize<ProviderSelectors>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (selectors?.AllowedHosts is not { Length: > 0 } ||
            selectors.AllowedHosts.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("Provider selector resource has no valid allowed hosts.");
        }

        return selectors;
    }
}
