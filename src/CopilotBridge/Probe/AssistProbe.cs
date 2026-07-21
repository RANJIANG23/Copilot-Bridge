using System.Text.Json;
using CopilotBridge.Browser;
using CopilotBridge.Core;

namespace CopilotBridge.Probe;

internal static class AssistProbe
{
    private const string Prompt = "这是 Copilot Bridge Phase 2 核心回归测试。请只回复：COPILOT_BRIDGE_PHASE2_OK";
    private const string ExpectedReply = "COPILOT_BRIDGE_PHASE2_OK";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<int> RunAsync(string? endpoint)
    {
        try
        {
            var settings = new BridgeSettings();
            var executor = new CopilotTurnExecutor(settings, ProviderSelectors.Load());
            var result = await executor.AssistAsync(new AssistRequest(Prompt), endpoint);
            var replyMatches = result.ReplyMarkdown.Trim().Equals(ExpectedReply, StringComparison.Ordinal);
            var singleTurn = result.UserMessageDelta == 1 && result.AssistantMessageDelta == 1;

            WriteJson(new
            {
                status = replyMatches && singleTurn ? "ok" : "completed_unexpected_result",
                canRetrySafely = false,
                result.Model,
                reply = result.ReplyMarkdown,
                result.ConversationUrl,
                result.UserMessageDelta,
                result.AssistantMessageDelta
            });
            return replyMatches && singleTurn ? 0 : 1;
        }
        catch (SubmissionUnknownException exception)
        {
            WriteJson(new
            {
                status = "submission_unknown",
                canRetrySafely = false,
                message = exception.Message
            });
            return 3;
        }
        catch (Exception exception)
        {
            WriteJson(new
            {
                status = "not_submitted",
                canRetrySafely = true,
                error = exception.GetType().Name,
                message = exception.Message
            });
            return 1;
        }
    }

    private static void WriteJson(object value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
}
