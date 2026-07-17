using CopilotBridge.Probe;

namespace CopilotBridge;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || !args.Contains("--probe", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("Copilot Bridge");
            Console.WriteLine("Usage: CopilotBridge.exe --probe [--verify-background-target] [--select-model] [--verify-test-turn] [--send-test] [--endpoint <url>]");
            return args.Length == 0 ? 0 : 2;
        }

        var options = ProbeOptions.Parse(args);
        return await EdgeProbe.RunAsync(options);
    }
}
