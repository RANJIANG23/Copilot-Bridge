using System.Collections.Concurrent;
using System.Text.Json;

namespace CopilotBridge.Core;

internal sealed class ConsultationLease : IDisposable
{
    private readonly FileStream _stream;

    private ConsultationLease(FileStream stream) => _stream = stream;

    internal static ConsultationLease? TryAcquire(string? path = null)
    {
        path ??= Path.Combine(AppDataDirectory(), "consultation.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            return new ConsultationLease(new FileStream(
                path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            return null;
        }
    }

    internal static bool IsBusy(string? path = null)
    {
        path ??= Path.Combine(AppDataDirectory(), "consultation.lock");
        if (!File.Exists(path)) return false;
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
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

internal sealed record ConsultationRecord
{
    public string Mode { get; init; } = "assist";
    public int TurnCount { get; init; }
    public int TurnBudget { get; init; }
    public string? PrimaryConversationUrl { get; init; }
    public string? ComplexityConversationUrl { get; init; }
    public string? EvidenceConversationUrl { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;
    public string Status { get; init; } = "completed";
    public string? LastModel { get; init; }
}

internal sealed class ConsultationStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly ConcurrentDictionary<string, ConsultationRecord> _memory =
        new(StringComparer.Ordinal);

    internal ConsultationStateStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopilotBridge",
            "consultations.json");
    }

    internal string FilePath => _path;

    internal async Task<ConsultationRecord?> FindAsync(
        string consultationId,
        CancellationToken cancellationToken = default)
    {
        _memory.TryGetValue(consultationId, out var remembered);
        var persisted = (await LoadAsync(cancellationToken)).Conversations.GetValueOrDefault(consultationId);
        var current = Newest(remembered, persisted);
        if (current is not null) _memory[consultationId] = current;
        return current;
    }

    internal async Task<ConsultationRecord?> FindMostRecentAsync(
        CancellationToken cancellationToken = default)
    {
        var state = await LoadAsync(cancellationToken);
        foreach (var item in _memory)
        {
            state.Conversations[item.Key] = Newest(
                item.Value,
                state.Conversations.GetValueOrDefault(item.Key))!;
        }
        return state.Conversations.Values
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefault();
    }

    internal async Task SaveAsync(
        string consultationId,
        ConsultationRecord record,
        CancellationToken cancellationToken = default)
    {
        var remembered = record with { UpdatedAt = DateTimeOffset.Now };
        _memory[consultationId] = remembered;
        var state = await LoadAsync(cancellationToken);
        state.Conversations[consultationId] = remembered;
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    internal async Task<string?> FindConversationAsync(
        string consultationId,
        CancellationToken cancellationToken = default) =>
        (await FindAsync(consultationId, cancellationToken))?.PrimaryConversationUrl;

    internal Task SaveConversationAsync(
        string consultationId,
        string conversationUrl,
        CancellationToken cancellationToken = default) =>
        SaveAsync(
            consultationId,
            new ConsultationRecord { PrimaryConversationUrl = conversationUrl },
            cancellationToken);

    private async Task<ConsultationState> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return new ConsultationState();
        await using var stream = new FileStream(
            _path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var state = new ConsultationState();
        if (!document.RootElement.TryGetProperty("conversations", out var conversations)) return state;

        foreach (var item in conversations.EnumerateObject())
        {
            state.Conversations[item.Name] = item.Value.ValueKind == JsonValueKind.String
                ? new ConsultationRecord { PrimaryConversationUrl = item.Value.GetString() }
                : item.Value.Deserialize<ConsultationRecord>(JsonOptions) ?? new ConsultationRecord();
        }

        return state;
    }

    private static ConsultationRecord? Newest(
        ConsultationRecord? first,
        ConsultationRecord? second) => (first, second) switch
        {
            (null, null) => null,
            (not null, null) => first,
            (null, not null) => second,
            _ when first!.TurnCount != second!.TurnCount =>
                first.TurnCount > second.TurnCount ? first : second,
            _ => first!.UpdatedAt >= second!.UpdatedAt ? first : second
        };

    private sealed class ConsultationState
    {
        public Dictionary<string, ConsultationRecord> Conversations { get; init; } =
            new(StringComparer.Ordinal);
    }
}
