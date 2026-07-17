using CopilotBridge.Probe;

namespace CopilotBridge;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var isProbe = args.Contains("--probe", StringComparer.OrdinalIgnoreCase);
        var isAssistTest = args.Contains("--assist-test", StringComparer.OrdinalIgnoreCase);
        if (args.Length == 0 || (!isProbe && !isAssistTest))
        {
            Console.WriteLine("Copilot Bridge");
            Console.WriteLine("Usage: CopilotBridge.exe (--probe [options] | --assist-test) [--endpoint <url>]");
            return args.Length == 0 ? 0 : 2;
        }

        var options = ProbeOptions.Parse(args);
        return isAssistTest
            ? await AssistProbe.RunAsync(options.Endpoint)
            : await EdgeProbe.RunAsync(options);
    }
}
