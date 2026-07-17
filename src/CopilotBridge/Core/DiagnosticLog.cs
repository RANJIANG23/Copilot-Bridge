using System.Text;

namespace CopilotBridge.Core;

internal static class DiagnosticLog
{
    internal static void Write(string eventName, Exception exception, string? path = null)
    {
        try
        {
            path ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CopilotBridge",
                "logs",
                $"bridge-{DateTime.Now:yyyyMMdd}.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var details = string.Join(
                " --> ",
                ExceptionChain(exception).Select(item =>
                    $"{item.GetType().Name}: {SingleLine(item.Message)}"));
            var line = $"{DateTimeOffset.Now:O} [{SingleLine(eventName)}] {details}{Environment.NewLine}";
            File.AppendAllText(path, line, new UTF8Encoding(false));
        }
        catch
        {
            // Diagnostics must never alter consultation behavior.
        }
    }

    private static IEnumerable<Exception> ExceptionChain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static string SingleLine(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
