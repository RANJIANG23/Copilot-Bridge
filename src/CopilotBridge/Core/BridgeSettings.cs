using System.Text.Json;
using System.Text.Json.Serialization;

namespace CopilotBridge.Core;

internal enum ConsultationPolicy
{
    Disabled,
    ManualOnly,
    CodexMayConsult,
    RequiredForKeyDesign
}

internal enum CollaborationMode
{
    Assist,
    Outsource,
    Review
}

internal enum AppLanguage
{
    Chinese,
    English
}

internal enum AppTheme
{
    Light,
    Dark
}

internal static class CollaborationBudgetOptions
{
    internal const int Minimum = 1;
    internal const int Maximum = 20;
    internal const int DefaultAssist = 2;
    internal const int DefaultOutsource = 6;
    internal const int DefaultReview = 1;

    internal static int ForMode(BridgeSettings settings, CollaborationMode mode) => mode switch
    {
        CollaborationMode.Outsource => settings.OutsourceTurnBudget,
        CollaborationMode.Review => settings.ReviewTurnBudget,
        _ => settings.AssistTurnBudget
    };

    internal static int RecordedTurns(ConversationDocument document, CollaborationMode mode)
    {
        var deliveries = document.Turns.Count(turn =>
            turn.Role.Equals("agent", StringComparison.OrdinalIgnoreCase));
        return mode == CollaborationMode.Review ? (deliveries + 1) / 2 : deliveries;
    }
}

internal static class ModelPriorityOptions
{
    internal static readonly IReadOnlyList<string> Default =
        ["Opus", "GPT 5.6 Think deeper", "深度思考"];

    internal static string Serialize(IEnumerable<string> priority) => string.Join("|", priority);

    internal static IReadOnlyList<string> Parse(string? priority) =>
        string.IsNullOrWhiteSpace(priority)
            ? Default
            : priority.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    internal static bool IsValid(string? priority)
    {
        var values = Parse(priority);
        return values.Count == Default.Count &&
               values.OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(Default.OrderBy(value => value, StringComparer.Ordinal), StringComparer.Ordinal);
    }
}

internal sealed record BridgeSettings
{
    public string EdgeUserDataDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft",
        "Edge",
        "User Data");

    public int MenuMinimumWaitMilliseconds { get; init; } = 2_000;

    public int MenuMaximumWaitMilliseconds { get; init; } = 6_000;

    public int ReplyTimeoutSeconds { get; init; } = 300;

    public double StatisticsTokenMultiplier { get; init; } = UsageStatisticsCalculator.DefaultMultiplier;

    public string ModelPriority { get; init; } = ModelPriorityOptions.Serialize(ModelPriorityOptions.Default);

    public ConsultationPolicy ConsultationPolicy { get; init; } = ConsultationPolicy.ManualOnly;

    public CollaborationMode CollaborationMode { get; init; } = CollaborationMode.Assist;

    public int AssistTurnBudget { get; init; } = CollaborationBudgetOptions.DefaultAssist;

    public int OutsourceTurnBudget { get; init; } = CollaborationBudgetOptions.DefaultOutsource;

    public int ReviewTurnBudget { get; init; } = CollaborationBudgetOptions.DefaultReview;

    public AppLanguage DisplayLanguage { get; init; } = AppLanguage.Chinese;

    public AppTheme Theme { get; init; } = AppTheme.Light;

    public bool KeepMcpRunningInBackground { get; init; } = true;

    public bool UseSystemTray { get; init; }

    public string? BoundConversationUrl { get; init; }

    public string ConversationWorkspaceDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CopilotBridge",
        "workspace");

    public bool StoreConversationContent { get; init; } = true;
}

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    static SettingsStore()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    internal SettingsStore(string? path = null)
    {
        FilePath = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopilotBridge",
            "settings.json");
    }

    internal string FilePath { get; }

    internal async Task<BridgeSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return new BridgeSettings();
        }

        var json = await File.ReadAllTextAsync(FilePath, cancellationToken);
        var settings = Validate(JsonSerializer.Deserialize<BridgeSettings>(json, JsonOptions) ?? new BridgeSettings());
        if (json.Contains("\"conversationTurnLimit\"", StringComparison.OrdinalIgnoreCase))
        {
            await SaveAsync(settings, cancellationToken);
        }
        return settings;
    }

    internal async Task SaveAsync(
        BridgeSettings settings,
        CancellationToken cancellationToken = default)
    {
        settings = Validate(settings);
        var directory = Path.GetDirectoryName(FilePath) ??
                        throw new InvalidOperationException("Settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = FilePath + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, FilePath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static BridgeSettings Validate(BridgeSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.EdgeUserDataDirectory))
        {
            throw new InvalidDataException("Edge user-data directory is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.ConversationWorkspaceDirectory))
        {
            throw new InvalidDataException("Conversation workspace directory is required.");
        }

        if (settings.MenuMinimumWaitMilliseconds < 0 ||
            settings.MenuMaximumWaitMilliseconds < settings.MenuMinimumWaitMilliseconds ||
            settings.ReplyTimeoutSeconds <= 0 ||
            settings.StatisticsTokenMultiplier is < UsageStatisticsCalculator.MinimumMultiplier or
                > UsageStatisticsCalculator.MaximumMultiplier)
        {
            throw new InvalidDataException("Settings contain invalid timeout values.");
        }

        if (!ModelPriorityOptions.IsValid(settings.ModelPriority))
        {
            throw new InvalidDataException("Settings contain an invalid model priority.");
        }

        if (!IsValidBudget(settings.AssistTurnBudget) ||
            !IsValidBudget(settings.OutsourceTurnBudget) ||
            !IsValidBudget(settings.ReviewTurnBudget))
        {
            throw new InvalidDataException(
                $"Collaboration turn budgets must be between {CollaborationBudgetOptions.Minimum} and {CollaborationBudgetOptions.Maximum}.");
        }

        return settings;
    }

    private static bool IsValidBudget(int budget) =>
        budget is >= CollaborationBudgetOptions.Minimum and <= CollaborationBudgetOptions.Maximum;
}
