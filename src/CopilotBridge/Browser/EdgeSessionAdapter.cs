using System.Text.Json;
using CopilotBridge.Core;
using Microsoft.Playwright;

namespace CopilotBridge.Browser;

internal sealed class EdgeSessionAdapter : IAsyncDisposable
{
    private readonly IPlaywright _playwright;

    private EdgeSessionAdapter(
        IPlaywright playwright,
        IBrowser browser,
        IPage page,
        string endpoint)
    {
        _playwright = playwright;
        Browser = browser;
        Page = page;
        Endpoint = endpoint;
    }

    internal IBrowser Browser { get; }

    internal IPage Page { get; }

    internal string Endpoint { get; }

    internal static async Task<EdgeSessionAdapter> ConnectAsync(
        BridgeSettings settings,
        ProviderSelectors selectors,
        string? endpointOverride = null,
        int timeoutMilliseconds = 10_000)
    {
        var endpoint = endpointOverride ?? ResolveEndpoint(settings.EdgeUserDataDirectory);
        var playwright = await Playwright.CreateAsync();

        try
        {
            var browser = await playwright.Chromium.ConnectOverCDPAsync(
                endpoint,
                new BrowserTypeConnectOverCDPOptions { Timeout = timeoutMilliseconds });
            var page = FindSingleCopilotPage(browser, selectors.AllowedHost);
            return new EdgeSessionAdapter(playwright, browser, page, endpoint);
        }
        catch
        {
            playwright.Dispose();
            throw;
        }
    }

    internal static string ResolveEndpoint(string edgeUserDataDirectory)
    {
        var portFile = Path.Combine(edgeUserDataDirectory, "DevToolsActivePort");
        if (!File.Exists(portFile))
        {
            throw new InvalidOperationException(
                "Daily Edge has no DevToolsActivePort. Enable remote debugging for this Edge instance.");
        }

        var lines = File.ReadAllLines(portFile);
        if (!int.TryParse(lines.FirstOrDefault()?.Trim(), out var port) || port is <= 0 or > 65535)
        {
            throw new InvalidDataException("DevToolsActivePort contains an invalid port.");
        }

        var browserPath = lines.ElementAtOrDefault(1)?.Trim();
        if (browserPath is null ||
            !browserPath.StartsWith("/devtools/browser/", StringComparison.Ordinal) ||
            !Uri.TryCreate($"ws://127.0.0.1:{port}{browserPath}", UriKind.Absolute, out var endpoint))
        {
            throw new InvalidDataException("DevToolsActivePort contains no valid browser WebSocket path.");
        }

        return endpoint.AbsoluteUri;
    }

    internal async Task<string> VerifyBackgroundTargetAsync()
    {
        var context = Page.Context;
        var existingPages = context.Pages.ToArray();
        var visibilityBefore = await ReadVisibilityAsync(existingPages);
        var browserSession = await Browser.NewBrowserCDPSessionAsync();
        string? targetId = null;

        try
        {
            var arguments = new Dictionary<string, object>
            {
                ["url"] = "about:blank",
                ["background"] = true
            };
            var contextId = await ReadBrowserContextIdAsync(context);
            if (!string.IsNullOrWhiteSpace(contextId))
            {
                arguments["browserContextId"] = contextId;
            }

            var result = await browserSession.SendAsync("Target.createTarget", arguments);
            targetId = result is JsonElement payload
                ? payload.GetProperty("targetId").GetString()
                : null;
            if (string.IsNullOrWhiteSpace(targetId))
            {
                throw new InvalidOperationException("CDP did not return a background targetId.");
            }

            await Task.Delay(500);
            var visibilityAfter = await ReadVisibilityAsync(existingPages);
            if (!visibilityBefore.SequenceEqual(visibilityAfter))
            {
                throw new InvalidOperationException("A background target changed existing page visibility.");
            }

            return "Target.createTarget(background=true) preserved all existing page visibility states.";
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(targetId))
            {
                await browserSession.SendAsync(
                    "Target.closeTarget",
                    new Dictionary<string, object> { ["targetId"] = targetId });
            }

            await browserSession.DetachAsync();
        }
    }

    public ValueTask DisposeAsync()
    {
        _playwright.Dispose();
        return ValueTask.CompletedTask;
    }

    private static IPage FindSingleCopilotPage(IBrowser browser, string allowedHost)
    {
        var matches = browser.Contexts
            .SelectMany(context => context.Pages)
            .Where(page => IsAllowedCopilotUrl(page.Url, allowedHost))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException("No eligible Microsoft 365 Copilot chat tab was found."),
            _ => throw new InvalidOperationException(
                $"Found {matches.Length} eligible Copilot tabs. Keep exactly one dedicated chat tab open.")
        };
    }

    private static bool IsAllowedCopilotUrl(string value, string allowedHost)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals(allowedHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return uri.AbsolutePath.Equals("/chat", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.StartsWith("/chat/", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> ReadBrowserContextIdAsync(IBrowserContext context)
    {
        var page = context.Pages.FirstOrDefault() ??
                   throw new InvalidOperationException("The target browser context contains no pages.");
        var session = await context.NewCDPSessionAsync(page);

        try
        {
            var result = await session.SendAsync("Target.getTargetInfo");
            return result is JsonElement payload &&
                   payload.TryGetProperty("targetInfo", out var targetInfo) &&
                   targetInfo.TryGetProperty("browserContextId", out var browserContextId)
                ? browserContextId.GetString()
                : null;
        }
        finally
        {
            await session.DetachAsync();
        }
    }

    private static async Task<string[]> ReadVisibilityAsync(IEnumerable<IPage> pages)
    {
        var states = new List<string>();
        foreach (var page in pages)
        {
            states.Add($"{page.Url}|{await page.EvaluateAsync<string>("document.visibilityState")}");
        }

        return states.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }
}
