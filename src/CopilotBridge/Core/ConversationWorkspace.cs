using System.Text;
using System.Text.Json;

namespace CopilotBridge.Core;

internal sealed record WorkspaceProject(string Id, string Name, bool IsSystem, string DirectoryPath);

internal sealed record ConversationTurn(
    DateTimeOffset Timestamp,
    string Role,
    string Markdown,
    string? Model = null,
    string ModelStatus = "not_applicable",
    string? Reviewer = null);

internal sealed record ConversationDocument
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; init; } = ConversationWorkspaceStore.InboxProjectId;
    public string? CopilotConversationId { get; init; }
    public string? CopilotConversationUrl { get; init; }
    public string CopilotTitleInitial { get; init; } = "未命名 Copilot 对话";
    public string CopilotTitleCurrent { get; init; } = "未命名 Copilot 对话";
    public IReadOnlyList<string> CopilotTitleHistory { get; init; } = [];
    public string? LocalTitle { get; init; }
    public string TitleSource { get; init; } = "copilot";
    public string Mode { get; init; } = "assist";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;
    public IReadOnlyList<ConversationTurn> Turns { get; init; } = [];

    public string DisplayTitle => string.IsNullOrWhiteSpace(LocalTitle)
        ? CopilotTitleCurrent
        : LocalTitle;
}

internal sealed record ConversationSummary(
    string Id,
    string ProjectId,
    string DisplayTitle,
    string CopilotTitle,
    string? LastModel,
    DateTimeOffset UpdatedAt,
    int TurnCount);

internal sealed record ConversationSearchResult(
    ConversationTurn Turn,
    string Snippet);

internal sealed class ConversationWorkspaceStore
{
    internal const string InboxProjectId = "收件箱";
    internal const string StandaloneProjectId = "独立对话";
    private const string ProjectMarker = ".bridge-project.md";
    private const string MetadataPrefix = "<!-- copilot-bridge-conversation:";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _rootDirectory;

    internal ConversationWorkspaceStore(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopilotBridge",
            "workspace");
    }

    internal string RootDirectory => _rootDirectory;

    internal async Task<IReadOnlyList<WorkspaceProject>> GetProjectsAsync(
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_rootDirectory);
        var projects = new List<WorkspaceProject>
        {
            await EnsureProjectAsync(InboxProjectId, InboxProjectId, true, cancellationToken),
            await EnsureProjectAsync(StandaloneProjectId, StandaloneProjectId, true, cancellationToken)
        };

        foreach (var directory in Directory.EnumerateDirectories(_rootDirectory))
        {
            var name = Path.GetFileName(directory);
            if (name is InboxProjectId or StandaloneProjectId ||
                !File.Exists(Path.Combine(directory, ProjectMarker)))
            {
                continue;
            }

            projects.Add(new WorkspaceProject(name, name, false, directory));
        }

        return projects.OrderBy(project => project.IsSystem ? 0 : 1)
            .ThenBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    internal async Task<WorkspaceProject> CreateProjectAsync(
        string requestedName,
        CancellationToken cancellationToken = default)
    {
        var name = NormalizeProjectName(requestedName);
        Directory.CreateDirectory(_rootDirectory);
        var candidate = name;
        var suffix = 2;
        while (Directory.Exists(Path.Combine(_rootDirectory, candidate)))
        {
            candidate = $"{name} {suffix++}";
        }

        return await EnsureProjectAsync(candidate, candidate, false, cancellationToken);
    }

    internal async Task<ConversationDocument> CreateConversationAsync(
        string projectId,
        string? localTitle = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureKnownProjectAsync(projectId, cancellationToken);
        var now = DateTimeOffset.Now;
        var document = new ConversationDocument
        {
            ProjectId = projectId,
            LocalTitle = string.IsNullOrWhiteSpace(localTitle) ? null : localTitle.Trim(),
            TitleSource = string.IsNullOrWhiteSpace(localTitle) ? "copilot" : "local_override",
            CreatedAt = now,
            UpdatedAt = now
        };
        await SaveAsync(document, cancellationToken);
        return document;
    }

    internal async Task<IReadOnlyList<ConversationSummary>> GetConversationsAsync(
        string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        await GetProjectsAsync(cancellationToken);
        var documents = new List<ConversationDocument>();
        foreach (var path in Directory.EnumerateFiles(_rootDirectory, "*.md", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(path).Equals(ProjectMarker, StringComparison.OrdinalIgnoreCase)) continue;
            var document = await TryLoadPathAsync(path, cancellationToken);
            if (document is not null && (projectId is null || document.ProjectId == projectId))
            {
                documents.Add(document);
            }
        }

        return documents.OrderByDescending(document => document.UpdatedAt)
            .Select(document => new ConversationSummary(
                document.Id,
                document.ProjectId,
                document.DisplayTitle,
                document.CopilotTitleCurrent,
                document.Turns.LastOrDefault(turn => !string.IsNullOrWhiteSpace(turn.Model))?.Model,
                document.UpdatedAt,
                document.Turns.Count))
            .ToArray();
    }

    internal async Task<ConversationDocument?> FindAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        var path = FindPath(conversationId);
        return path is null ? null : await TryLoadPathAsync(path, cancellationToken);
    }

    internal async Task<ConversationDocument> RenameAsync(
        ConversationDocument document,
        string localTitle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localTitle))
        {
            throw new InvalidDataException("本地会话名称不能为空。");
        }

        document = document with
        {
            LocalTitle = localTitle.Trim(),
            TitleSource = "local_override",
            UpdatedAt = DateTimeOffset.Now
        };
        await SaveAsync(document, cancellationToken);
        return document;
    }

    internal async Task<ConversationDocument> MoveAsync(
        ConversationDocument document,
        string destinationProjectId,
        CancellationToken cancellationToken = default)
    {
        await EnsureKnownProjectAsync(destinationProjectId, cancellationToken);
        var oldPath = FindPath(document.Id);
        document = document with { ProjectId = destinationProjectId, UpdatedAt = DateTimeOffset.Now };
        await SaveAsync(document, cancellationToken);
        if (oldPath is not null && File.Exists(oldPath) &&
            !oldPath.Equals(PathFor(document), StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(oldPath);
        }

        return document;
    }

    internal async Task<ConversationDocument> AppendRunAsync(
        ConversationDocument document,
        CollaborationRunResult result,
        CancellationToken cancellationToken = default)
    {
        var turns = document.Turns.ToList();
        foreach (var response in result.Responses)
        {
            turns.Add(new ConversationTurn(
                DateTimeOffset.Now,
                "agent",
                response.RequestMarkdown,
                null,
                "not_applicable",
                response.Reviewer));
            turns.Add(new ConversationTurn(
                DateTimeOffset.Now,
                "copilot",
                response.Result.ReplyMarkdown,
                response.Result.Model,
                "verified",
                response.Reviewer));
        }

        var url = result.PrimaryConversationUrl ?? result.Responses.LastOrDefault()?.Result.ConversationUrl;
        document = document with
        {
            CopilotConversationUrl = url ?? document.CopilotConversationUrl,
            CopilotConversationId = ExtractConversationId(url) ?? document.CopilotConversationId,
            Mode = document.Mode,
            Turns = turns,
            UpdatedAt = DateTimeOffset.Now
        };
        await SaveAsync(document, cancellationToken);
        return document;
    }

    internal IReadOnlyList<ConversationSearchResult> Search(ConversationDocument document, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        return document.Turns
            .Where(turn => turn.Markdown.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Select(turn => new ConversationSearchResult(turn, BuildSnippet(turn.Markdown, query)))
            .ToArray();
    }

    internal async Task SaveAsync(ConversationDocument document, CancellationToken cancellationToken = default)
    {
        await EnsureKnownProjectAsync(document.ProjectId, cancellationToken);
        var path = PathFor(document);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        var markdown = Render(document);
        try
        {
            await File.WriteAllTextAsync(temporaryPath, markdown, new UTF8Encoding(false), cancellationToken);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    internal string Render(ConversationDocument document)
    {
        var encoded = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions));
        var builder = new StringBuilder();
        builder.Append(MetadataPrefix).Append(encoded).AppendLine(" -->");
        builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine("schema: copilot-bridge-conversation/v1.1");
        builder.AppendLine($"conversation_id: {document.Id}");
        builder.AppendLine($"copilot_url: {document.CopilotConversationUrl ?? ""}");
        builder.AppendLine($"copilot_title_initial: {document.CopilotTitleInitial}");
        builder.AppendLine($"copilot_title_current: {document.CopilotTitleCurrent}");
        builder.AppendLine($"local_title: {document.LocalTitle ?? ""}");
        builder.AppendLine($"project: {document.ProjectId}");
        builder.AppendLine($"created_at: {document.CreatedAt:O}");
        builder.AppendLine($"updated_at: {document.UpdatedAt:O}");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.Append("# ").AppendLine(document.DisplayTitle);
        builder.AppendLine();
        builder.AppendLine($"> Copilot：{document.CopilotTitleCurrent}  ");
        builder.AppendLine($"> 项目：{document.ProjectId}");

        foreach (var turn in document.Turns)
        {
            builder.AppendLine();
            builder.Append("## ").Append(turn.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"))
                .Append(" · ").Append(turn.Role == "copilot" ? "Microsoft 365 Copilot" : "用户 / Agent");
            if (!string.IsNullOrWhiteSpace(turn.Reviewer)) builder.Append(" · ").Append(turn.Reviewer);
            builder.AppendLine();
            if (turn.Role == "copilot")
            {
                builder.AppendLine();
                builder.Append("- 实际模型：`").Append(turn.Model ?? "未知").AppendLine("`");
                builder.Append("- 模型状态：`").Append(turn.ModelStatus).AppendLine("`");
            }
            builder.AppendLine();
            builder.AppendLine(turn.Markdown.Trim());
        }

        return builder.ToString();
    }

    private async Task<WorkspaceProject> EnsureProjectAsync(
        string id,
        string name,
        bool isSystem,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_rootDirectory, id);
        Directory.CreateDirectory(path);
        var marker = Path.Combine(path, ProjectMarker);
        if (!File.Exists(marker))
        {
            await File.WriteAllTextAsync(marker, $"# {name}\n\nCopilot Bridge 项目目录。\n", new UTF8Encoding(false), cancellationToken);
        }
        return new WorkspaceProject(id, name, isSystem, path);
    }

    private async Task EnsureKnownProjectAsync(string projectId, CancellationToken cancellationToken)
    {
        var projects = await GetProjectsAsync(cancellationToken);
        if (projects.All(project => !project.Id.Equals(projectId, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("目标项目不存在。");
        }
    }

    private string PathFor(ConversationDocument document) => Path.Combine(
        _rootDirectory,
        document.ProjectId,
        $"conversation-{document.Id}.md");

    private string? FindPath(string conversationId)
    {
        if (!Directory.Exists(_rootDirectory)) return null;
        return Directory.EnumerateFiles(_rootDirectory, $"conversation-{conversationId}.md", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private async Task<ConversationDocument?> TryLoadPathAsync(string path, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        var end = text.IndexOf(" -->", StringComparison.Ordinal);
        if (!text.StartsWith(MetadataPrefix, StringComparison.Ordinal) || end < 0) return null;
        try
        {
            var encoded = text[MetadataPrefix.Length..end];
            return JsonSerializer.Deserialize<ConversationDocument>(Convert.FromBase64String(encoded), JsonOptions);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    private static string NormalizeProjectName(string value)
    {
        var name = value.Trim();
        if (name.Length == 0) throw new InvalidDataException("项目名称不能为空。");
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, ' ');
        name = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0 || name is "." or "..") throw new InvalidDataException("项目名称无效。");
        return name.Length > 80 ? name[..80] : name;
    }

    private static string? ExtractConversationId(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 3 && segments[^2].Equals("conversation", StringComparison.OrdinalIgnoreCase)
            ? segments[^1]
            : null;
    }

    private static string BuildSnippet(string markdown, string query)
    {
        var index = markdown.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0) return markdown;
        var start = Math.Max(0, index - 40);
        var length = Math.Min(markdown.Length - start, query.Length + 80);
        return (start > 0 ? "…" : string.Empty) + markdown.Substring(start, length) +
               (start + length < markdown.Length ? "…" : string.Empty);
    }
}
