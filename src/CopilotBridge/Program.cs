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
            return McpHost.RunAsync().GetAwaiter().GetResult();
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
