using System.Runtime.InteropServices;
using System.Text.Json;
using CopilotBridge.Browser;
using CopilotBridge.Core;

namespace CopilotBridge.Probe;

internal sealed record ProbeOptions(
    string? Endpoint,
    bool VerifyBackgroundTarget,
    bool SelectModel,
    bool VerifyTestTurn,
    bool SendTest)
{
    internal static ProbeOptions Parse(string[] args)
    {
        string? endpoint = null;

        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].Equals("--endpoint", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("--endpoint requires a WebSocket URL.");
            }

            endpoint = args[++index];
        }

        return new ProbeOptions(
            endpoint,
            args.Contains("--verify-background-target", StringComparer.OrdinalIgnoreCase),
            args.Contains("--select-model", StringComparer.OrdinalIgnoreCase),
            args.Contains("--verify-test-turn", StringComparer.OrdinalIgnoreCase),
            args.Contains("--send-test", StringComparer.OrdinalIgnoreCase));
    }
}

internal static class EdgeProbe
{
    internal const string TestPrompt = "这是 Copilot Bridge 的连接测试。请只回复：COPILOT_BRIDGE_TEST_OK";
    private const string TestReply = "COPILOT_BRIDGE_TEST_OK";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<int> RunAsync(ProbeOptions options)
    {
        var foregroundBefore = NativeMethods.GetForegroundWindow();
        NativeMethods.GetCursorPos(out var cursorBefore);

        try
        {
            var settings = new BridgeSettings();
            var selectors = ProviderSelectors.Load();
            await using var session = await EdgeSessionAdapter.ConnectAsync(
                settings,
                selectors,
                options.Endpoint);
            var driver = new CopilotPageDriver(session.Page, selectors, settings);

            var backgroundEvidence = options.VerifyBackgroundTarget
                ? await session.VerifyBackgroundTargetAsync()
                : null;
            string? selectedModel = null;
            PageTurnResult? turn = null;

            if (options.SelectModel || options.SendTest)
            {
                selectedModel = await driver.SelectAllowedModelAsync();
            }

            if (options.SendTest)
            {
                turn = await driver.SendAndReadAsync(TestPrompt);
            }

            object? testTurnEvidence = null;
            if (options.VerifyTestTurn)
            {
                var counts = await driver.CountExactTurnAsync(TestPrompt, TestReply);
                if (counts.PromptCount != 1 || counts.ReplyCount != 1)
                {
                    throw new InvalidOperationException(
                        $"Expected one test prompt/reply; found {counts.PromptCount}/{counts.ReplyCount}.");
                }

                testTurnEvidence = new
                {
                    promptCount = counts.PromptCount,
                    replyCount = counts.ReplyCount
                };
            }

            NativeMethods.GetCursorPos(out var cursorAfter);
            var foregroundAfter = NativeMethods.GetForegroundWindow();
            var foregroundPreserved = foregroundBefore == foregroundAfter;
            var cursorPreserved = cursorBefore.Equals(cursorAfter);
            var externalInteractionObserved = !cursorPreserved;
            var possibleForegroundSteal = !foregroundPreserved && cursorPreserved;

            WriteJson(new
            {
                status = "ok",
                endpoint = session.Endpoint,
                page = new { url = session.Page.Url, title = await session.Page.TitleAsync() },
                backgroundTarget = backgroundEvidence,
                selectedModel,
                submitted = turn is not null,
                reply = turn?.ReplyMarkdown,
                testTurnEvidence,
                foregroundPreserved,
                cursorPreserved,
                externalInteractionObserved,
                possibleForegroundSteal,
                observations = new
                {
                    foregroundBefore = foregroundBefore.ToInt64(),
                    foregroundAfter = foregroundAfter.ToInt64(),
                    cursorBefore = new { x = cursorBefore.X, y = cursorBefore.Y },
                    cursorAfter = new { x = cursorAfter.X, y = cursorAfter.Y }
                }
            });

            return possibleForegroundSteal ? 1 : 0;
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

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out Point point);
}

[StructLayout(LayoutKind.Sequential)]
internal struct Point : IEquatable<Point>
{
    internal int X;
    internal int Y;

    public readonly bool Equals(Point other) => X == other.X && Y == other.Y;
}
