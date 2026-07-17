using System.Text.Json;

namespace CopilotBridge.Core;

internal sealed class ConsultationLease : IDisposable
{
    private readonly FileStream _stream;

    private ConsultationLease(FileStream stream)
    {
        _stream = stream;
    }

    internal static ConsultationLease? TryAcquire(string? path = null)
    {
        path ??= Path.Combine(AppDataDirectory(), "consultation.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            return new ConsultationLease(new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None));
        }
        catch (IOException)
        {
            return null;
        }
    }

    internal static bool IsBusy(string? path = null)
    {
        path ??= Path.Combine(AppDataDirectory(), "consultation.lock");
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    public void Dispose() => _stream.Dispose();

    private static string AppDataDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CopilotBridge");
}

internal sealed class ConsultationStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;

    internal ConsultationStateStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopilotBridge",
            "consultations.json");
    }

    internal async Task<string?> FindConversationAsync(
        string consultationId,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(cancellationToken);
        return state.Conversations.GetValueOrDefault(consultationId);
    }

    internal async Task SaveConversationAsync(
        string consultationId,
        string conversationUrl,
        CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(cancellationToken);
        state.Conversations[consultationId] = conversationUrl;
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task<ConsultationState> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new ConsultationState();
        }

        await using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<ConsultationState>(stream, JsonOptions, cancellationToken) ??
               new ConsultationState();
    }

    private sealed class ConsultationState
    {
        public Dictionary<string, string> Conversations { get; init; } =
            new(StringComparer.Ordinal);
    }
}
