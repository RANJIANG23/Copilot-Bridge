using System.Diagnostics;
using System.Text.Json;

namespace CopilotBridge.Core;

internal sealed record McpProcessRegistration(
    int ProcessId,
    string ExecutablePath,
    DateTimeOffset StartedAt);

internal sealed class McpProcessRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private const string RegistryMutexName = "Local\\CopilotBridge.McpProcessRegistry";

    private readonly string _filePath;

    internal McpProcessRegistry(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopilotBridge",
            "mcp-processes.json");
    }

    internal IDisposable RegisterCurrentProcess()
    {
        using var process = Process.GetCurrentProcess();
        var executablePath = Environment.ProcessPath ?? process.MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine the MCP executable path.");
        Register(new McpProcessRegistration(process.Id, executablePath, process.StartTime));
        return new RegistrationLease(this, process.Id);
    }

    internal void Register(McpProcessRegistration registration)
    {
        WithRegistryLock(() =>
        {
            var registrations = PruneExitedRegistrations(Load())
                .Where(item => item.ProcessId != registration.ProcessId)
                .Append(registration)
                .ToArray();
            Save(registrations);
        });
    }

    internal void Unregister(int processId) => WithRegistryLock(() =>
        Save(PruneExitedRegistrations(Load()).Where(item => item.ProcessId != processId)));

    internal IReadOnlyList<McpProcessRegistration> GetLiveRegistrations(string expectedExecutablePath)
    {
        return WithRegistryLock(() =>
        {
            var expected = NormalizePath(expectedExecutablePath);
            var registrations = PruneExitedRegistrations(Load());
            Save(registrations);
            var live = new List<McpProcessRegistration>();
            foreach (var registration in registrations)
            {
                if (registration.ProcessId == Environment.ProcessId ||
                    !expected.Equals(NormalizePath(registration.ExecutablePath), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    using var process = Process.GetProcessById(registration.ProcessId);
                    if (!process.HasExited && expected.Equals(NormalizePath(process.MainModule?.FileName), StringComparison.OrdinalIgnoreCase))
                    {
                        live.Add(registration);
                    }
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }

            return (IReadOnlyList<McpProcessRegistration>)live;
        });
    }

    internal int TerminateRegisteredProcesses(string expectedExecutablePath)
    {
        var terminated = 0;
        var terminatedProcessIds = new HashSet<int>();
        foreach (var registration in GetLiveRegistrations(expectedExecutablePath))
        {
            try
            {
                using var process = Process.GetProcessById(registration.ProcessId);
                process.Kill(entireProcessTree: true);
                terminated++;
                terminatedProcessIds.Add(registration.ProcessId);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }

        WithRegistryLock(() => Save(PruneExitedRegistrations(Load())
            .Where(item => !terminatedProcessIds.Contains(item.ProcessId))));
        return terminated;
    }

    private IReadOnlyList<McpProcessRegistration> Load()
    {
        if (!File.Exists(_filePath)) return [];
        try
        {
            return JsonSerializer.Deserialize<McpProcessRegistration[]>(File.ReadAllText(_filePath), JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(IEnumerable<McpProcessRegistration> registrations)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("MCP registry path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = _filePath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(registrations, JsonOptions));
            File.Move(temporaryPath, _filePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static IEnumerable<McpProcessRegistration> PruneExitedRegistrations(
        IEnumerable<McpProcessRegistration> registrations) =>
        registrations.Where(registration =>
        {
            try
            {
                using var process = Process.GetProcessById(registration.ProcessId);
                return !process.HasExited;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        });

    private static T WithRegistryLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(false, RegistryMutexName);
        mutex.WaitOne();
        try { return action(); }
        finally { mutex.ReleaseMutex(); }
    }

    private static void WithRegistryLock(Action action) => WithRegistryLock(() =>
    {
        action();
        return true;
    });

    private static string NormalizePath(string? path) => Path.GetFullPath(path ?? string.Empty).TrimEnd('\\', '/');

    private sealed class RegistrationLease(McpProcessRegistry registry, int processId) : IDisposable
    {
        public void Dispose() => registry.Unregister(processId);
    }
}
