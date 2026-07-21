using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CopilotBridge.Core;

internal sealed class ConversationStorageV2
{
    private const string Schema = "copilot-bridge-conversation/v2";
    private const string StartMarker = "<!-- copilot-bridge-turn:{0}:{1:D4}:start -->";
    private const string ContentMarker = "<!-- copilot-bridge-turn:{0}:{1:D4}:content -->";
    private const string EndMarker = "<!-- copilot-bridge-turn:{0}:{1:D4}:end -->";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _rootDirectory;
    private readonly string _bridgeDirectory;
    private readonly string _conversationDirectory;

    internal ConversationStorageV2(string rootDirectory)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _bridgeDirectory = Path.Combine(_rootDirectory, ".bridge");
        _conversationDirectory = Path.Combine(_bridgeDirectory, "conversations");
    }

    internal string BridgeDirectory => _bridgeDirectory;
    internal string BackupsDirectory => Path.Combine(_bridgeDirectory, "backups");
    internal bool HasSidecar(string conversationId) => File.Exists(SidecarPath(conversationId));

    internal async Task<ConversationDocument?> TryLoadAsync(
        string markdownPath,
        CancellationToken cancellationToken)
    {
        var conversationId = ConversationIdFromPath(markdownPath);
        if (conversationId is null) return null;

        var markdown = await File.ReadAllTextAsync(markdownPath, cancellationToken);
        var markdownHash = Hash(markdown);
        var sidecar = await ReadSidecarAsync(SidecarPath(conversationId), cancellationToken);
        if (!IsMatching(sidecar, markdownPath, markdownHash))
        {
            var pending = await ReadSidecarAsync(PendingPath(conversationId), cancellationToken);
            sidecar = IsMatching(pending, markdownPath, markdownHash) ? pending : null;
        }
        if (sidecar is null) return null;

        var turns = new List<ConversationTurn>(sidecar.Turns.Count);
        for (var index = 0; index < sidecar.Turns.Count; index++)
        {
            var metadata = sidecar.Turns[index];
            var content = ExtractContent(markdown, sidecar.Id, index, metadata.ContentLength);
            if (content is null || !Hash(content).Equals(metadata.ContentSha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            turns.Add(new ConversationTurn(
                metadata.Timestamp,
                metadata.Role,
                content,
                metadata.Model,
                metadata.ModelStatus,
                metadata.Reviewer));
        }

        return new ConversationDocument
        {
            Id = sidecar.Id,
            ProjectId = sidecar.ProjectId,
            CopilotConversationId = sidecar.CopilotConversationId,
            CopilotConversationUrl = sidecar.CopilotConversationUrl,
            CopilotTitleInitial = sidecar.CopilotTitleInitial,
            CopilotTitleCurrent = sidecar.CopilotTitleCurrent,
            CopilotTitleHistory = sidecar.CopilotTitleHistory,
            LocalTitle = sidecar.LocalTitle,
            TitleSource = sidecar.TitleSource,
            Mode = sidecar.Mode,
            CreatedAt = sidecar.CreatedAt,
            UpdatedAt = sidecar.UpdatedAt,
            Turns = turns
        };
    }

    internal async Task SaveAsync(
        ConversationDocument document,
        string markdownPath,
        CancellationToken cancellationToken)
    {
        EnsureManagedDirectories();
        await RecoverPendingAsync(document.Id, cancellationToken);

        var markdown = Render(document, includeMarkers: true);
        var sidecar = CreateSidecar(document, markdownPath, markdown);
        var json = JsonSerializer.Serialize(sidecar, JsonOptions);
        var markdownTemporary = markdownPath + ".v2.tmp";
        var sidecarPath = SidecarPath(document.Id);
        var sidecarTemporary = sidecarPath + ".tmp";
        var pendingPath = PendingPath(document.Id);
        var pendingTemporary = pendingPath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(markdownPath)!);

        try
        {
            await File.WriteAllTextAsync(markdownTemporary, markdown, new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(sidecarTemporary, json, new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(pendingTemporary, json, new UTF8Encoding(false), cancellationToken);
            File.Move(pendingTemporary, pendingPath, true);
            File.Move(markdownTemporary, markdownPath, true);
            File.Move(sidecarTemporary, sidecarPath, true);
            File.Delete(pendingPath);
        }
        finally
        {
            DeleteIfExists(markdownTemporary);
            DeleteIfExists(sidecarTemporary);
            DeleteIfExists(pendingTemporary);
        }
    }

    internal async Task DeleteAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        await RecoverPendingAsync(conversationId, cancellationToken);
        DeleteIfExists(SidecarPath(conversationId));
        DeleteIfExists(PendingPath(conversationId));
    }

    internal async Task RecoverPendingAsync(string conversationId, CancellationToken cancellationToken)
    {
        var pendingPath = PendingPath(conversationId);
        if (!File.Exists(pendingPath)) return;
        var pending = await ReadSidecarAsync(pendingPath, cancellationToken);
        if (pending is null)
        {
            File.Delete(pendingPath);
            return;
        }

        var markdownPath = ResolveRelativePath(pending.MarkdownRelativePath);
        if (File.Exists(markdownPath))
        {
            var markdown = await File.ReadAllTextAsync(markdownPath, cancellationToken);
            if (Hash(markdown).Equals(pending.MarkdownSha256, StringComparison.OrdinalIgnoreCase))
            {
                var temporary = SidecarPath(conversationId) + ".recover.tmp";
                try
                {
                    await File.WriteAllTextAsync(
                        temporary,
                        JsonSerializer.Serialize(pending, JsonOptions),
                        new UTF8Encoding(false),
                        cancellationToken);
                    File.Move(temporary, SidecarPath(conversationId), true);
                }
                finally { DeleteIfExists(temporary); }
            }
        }
        File.Delete(pendingPath);
    }

    internal string RenderReadable(ConversationDocument document) => Render(document, includeMarkers: false);

    internal string? RelativeMarkdownPath(string conversationId)
    {
        var sidecar = ReadSidecarAsync(SidecarPath(conversationId), CancellationToken.None)
            .GetAwaiter().GetResult();
        return sidecar?.MarkdownRelativePath;
    }

    internal string SidecarFilePath(string conversationId) => SidecarPath(conversationId);
    internal string PendingFilePath(string conversationId) => PendingPath(conversationId);
    internal static string Sha256(string text) => Hash(text);

    internal void EnsureManagedDirectories()
    {
        Directory.CreateDirectory(_conversationDirectory);
        try
        {
            var attributes = File.GetAttributes(_bridgeDirectory);
            if (!attributes.HasFlag(FileAttributes.Hidden))
            {
                File.SetAttributes(_bridgeDirectory, attributes | FileAttributes.Hidden);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Hidden is a presentation preference; storage correctness does not depend on it.
        }
    }

    private ConversationSidecar CreateSidecar(
        ConversationDocument document,
        string markdownPath,
        string markdown)
    {
        var turns = document.Turns.Select(turn => new ConversationTurnSidecar(
            turn.Timestamp,
            turn.Role,
            turn.Model,
            turn.ModelStatus,
            turn.Reviewer,
            turn.Markdown.Length,
            Hash(turn.Markdown))).ToArray();
        return new ConversationSidecar(
            Schema,
            2,
            document.Id,
            document.ProjectId,
            RelativePath(markdownPath),
            document.CopilotConversationId,
            document.CopilotConversationUrl,
            document.CopilotTitleInitial,
            document.CopilotTitleCurrent,
            document.CopilotTitleHistory,
            document.LocalTitle,
            document.TitleSource,
            document.Mode,
            document.CreatedAt,
            document.UpdatedAt,
            Hash(markdown),
            turns);
    }

    private bool IsMatching(ConversationSidecar? sidecar, string markdownPath, string markdownHash)
    {
        if (sidecar is null || sidecar.Schema != Schema || sidecar.FormatVersion != 2) return false;
        if (!sidecar.MarkdownSha256.Equals(markdownHash, StringComparison.OrdinalIgnoreCase)) return false;
        return Path.GetFullPath(markdownPath).Equals(
            ResolveRelativePath(sidecar.MarkdownRelativePath),
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ConversationSidecar?> ReadSidecarAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<ConversationSidecar>(
                await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string Render(ConversationDocument document, bool includeMarkers)
    {
        var builder = new StringBuilder();
        builder.Append("# ").AppendLine(document.DisplayTitle).AppendLine();
        builder.Append("> Copilot：").Append(document.CopilotTitleCurrent).AppendLine("  ");
        builder.Append("> 项目：").AppendLine(document.ProjectId);

        for (var index = 0; index < document.Turns.Count; index++)
        {
            var turn = document.Turns[index];
            builder.AppendLine();
            if (includeMarkers) builder.AppendLine(string.Format(StartMarker, document.Id, index));
            builder.Append("## ").Append(turn.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"))
                .Append(" · ").Append(turn.Role == "copilot" ? "Microsoft 365 Copilot" : "用户 / Agent");
            if (!string.IsNullOrWhiteSpace(turn.Reviewer)) builder.Append(" · ").Append(turn.Reviewer);
            builder.AppendLine();
            if (turn.Role == "copilot")
            {
                builder.AppendLine();
                builder.Append("- 实际模型：`").Append(turn.Model ?? "unknown").AppendLine("`");
                builder.Append("- 模型状态：`").Append(turn.ModelStatus).AppendLine("`");
            }
            builder.AppendLine();
            if (includeMarkers) builder.AppendLine(string.Format(ContentMarker, document.Id, index));
            builder.Append(turn.Markdown);
            builder.AppendLine();
            if (includeMarkers) builder.AppendLine(string.Format(EndMarker, document.Id, index));
        }
        return builder.ToString();
    }

    private static string? ExtractContent(string markdown, string conversationId, int index, int contentLength)
    {
        var marker = string.Format(ContentMarker, conversationId, index);
        var markerIndex = markdown.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0) return null;
        var contentStart = markerIndex + marker.Length;
        if (contentStart < markdown.Length && markdown[contentStart] == '\r') contentStart++;
        if (contentStart < markdown.Length && markdown[contentStart] == '\n') contentStart++;
        if (contentLength < 0 || contentStart + contentLength > markdown.Length) return null;
        var content = markdown.Substring(contentStart, contentLength);
        var endMarker = string.Format(EndMarker, conversationId, index);
        var endIndex = contentStart + contentLength;
        if (endIndex < markdown.Length && markdown[endIndex] == '\r') endIndex++;
        if (endIndex < markdown.Length && markdown[endIndex] == '\n') endIndex++;
        return markdown.AsSpan(endIndex).StartsWith(endMarker, StringComparison.Ordinal) ? content : null;
    }

    private string RelativePath(string path)
    {
        var relative = Path.GetRelativePath(_rootDirectory, Path.GetFullPath(path));
        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Conversation path must stay inside the workspace.");
        }
        return relative.Replace('\\', '/');
    }

    private string ResolveRelativePath(string relativePath)
    {
        var resolved = Path.GetFullPath(Path.Combine(_rootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = _rootDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Conversation metadata points outside the workspace.");
        }
        return resolved;
    }

    private string SidecarPath(string conversationId) => Path.Combine(_conversationDirectory, $"{conversationId}.json");
    private string PendingPath(string conversationId) => Path.Combine(_conversationDirectory, $"{conversationId}.pending.json");

    private static string? ConversationIdFromPath(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith("conversation-", StringComparison.Ordinal)
            ? name["conversation-".Length..]
            : null;
    }

    private static string Hash(string text) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private sealed record ConversationSidecar(
        string Schema,
        int FormatVersion,
        string Id,
        string ProjectId,
        string MarkdownRelativePath,
        string? CopilotConversationId,
        string? CopilotConversationUrl,
        string CopilotTitleInitial,
        string CopilotTitleCurrent,
        IReadOnlyList<string> CopilotTitleHistory,
        string? LocalTitle,
        string TitleSource,
        string Mode,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string MarkdownSha256,
        IReadOnlyList<ConversationTurnSidecar> Turns);

    private sealed record ConversationTurnSidecar(
        DateTimeOffset Timestamp,
        string Role,
        string? Model,
        string ModelStatus,
        string? Reviewer,
        int ContentLength,
        string ContentSha256);
}
