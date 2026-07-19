using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using CopilotBridge.Mcp;
using CopilotBridge.Probe;
using CopilotBridge.UI;

namespace CopilotBridge;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--mcp", StringComparer.OrdinalIgnoreCase))
        {
            string? settingsPath;
            try
            {
                settingsPath = GetOption(args, "--settings-path");
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 2;
            }

            if (settingsPath is not null && !Path.IsPathFullyQualified(settingsPath))
            {
                Console.Error.WriteLine("--settings-path must be an absolute path.");
                return 2;
            }

            return McpHost.RunAsync(settingsPath).GetAwaiter().GetResult();
        }

        var isProbe = args.Contains("--probe", StringComparer.OrdinalIgnoreCase);
        var isAssistTest = args.Contains("--assist-test", StringComparer.OrdinalIgnoreCase);
        if (args.Length == 0 || (!isProbe && !isAssistTest))
        {
            if (args.Length > 0)
            {
                ConsoleHost.Attach();
                Console.WriteLine("Usage: CopilotBridge.exe (--mcp | --probe [options] | --assist-test) [--endpoint <url>]");
                return 2;
            }

            var application = new Application
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };
            return application.Run(new MainWindow());
        }

        ConsoleHost.Attach();
        var options = ProbeOptions.Parse(args);
        return isAssistTest
            ? AssistProbe.RunAsync(options.Endpoint).GetAwaiter().GetResult()
            : EdgeProbe.RunAsync(options).GetAwaiter().GetResult();
    }

    private static string? GetOption(IReadOnlyList<string> args, string option)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (!args[index].Equals(option, StringComparison.OrdinalIgnoreCase)) continue;
            if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
            {
                throw new ArgumentException($"{option} requires a value.");
            }

            return args[index + 1];
        }

        return null;
    }
}

internal static class ConsoleHost
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    internal static void Attach()
    {
        AttachConsole(AttachParentProcess);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);
}
