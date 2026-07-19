using System.Text;
using System.Text.Json;
using CopilotBridge.Browser;

namespace CopilotBridge.Core;

internal sealed record WorkspaceProject(
    string Id,
    string Name,
    bool IsSystem,
    string DirectoryPath,
    bool IsPinned = false,
    int SortOrder = int.MaxValue);

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
    public string ProjectId { get; init; } = ConversationWorkspaceStore.StandaloneProjectId;
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
    internal const string StandaloneProjectId = "未分类对话";
    private const string LegacyInboxProjectId = "收件箱";
    private const string LegacyStandaloneProjectId = "独立对话";
    private const string ProjectMarker = ".bridge-project.md";
    private const string ProjectMetadataPrefix = "<!-- copilot-bridge-project:";
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
        await MigrateSystemProjectsAsync(cancellationToken);
        var projects = new List<WorkspaceProject>
        {
            await EnsureProjectAsync(StandaloneProjectId, StandaloneProjectId, true, cancellationToken)
        };

        foreach (var directory in Directory.EnumerateDirectories(_rootDirectory))
        {
            var name = Path.GetFileName(directory);
            if (name is LegacyInboxProjectId or StandaloneProjectId or LegacyStandaloneProjectId ||
                !File.Exists(Path.Combine(directory, ProjectMarker)))
            {
                continue;
            }

            var metadata = await ReadProjectMarkerAsync(Path.Combine(directory, ProjectMarker), cancellationToken);
            projects.Add(new WorkspaceProject(
                name,
                name,
                false,
                directory,
                metadata.IsPinned,
                NormalizeSortOrder(metadata.SortOrder)));
        }

        return projects.OrderBy(project => SystemProjectOrder(project.Id))
            .ThenByDescending(project => project.IsPinned)
            .ThenBy(project => project.SortOrder)
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

        var project = await EnsureProjectAsync(candidate, candidate, false, cancellationToken);
        var nextOrder = await GetNextProjectSortOrderAsync(cancellationToken);
        await WriteProjectMarkerAsync(
            Path.Combine(project.DirectoryPath, ProjectMarker),
            project.Name,
            project.IsPinned,
            nextOrder,
            cancellationToken);
        return project with { SortOrder = nextOrder };
    }

    internal async Task<WorkspaceProject> RenameProjectAsync(
        WorkspaceProject project,
        string requestedName,
        CancellationToken cancellationToken = default)
    {
        var current = await GetCustomProjectAsync(project.Id, cancellationToken);
        var newId = NormalizeProjectName(requestedName);
        if (newId.Equals(current.Id, StringComparison.Ordinal)) return current;

        var destinationDirectory = Path.Combine(_rootDirectory, newId);
        if (Directory.Exists(destinationDirectory))
        {
            throw new InvalidDataException("项目名称已存在。");
        }

        var documents = new List<ConversationDocument>();
        foreach (var path in Directory.EnumerateFiles(current.DirectoryPath, "conversation-*.md"))
        {
            var document = await TryLoadPathAsync(path, cancellationToken);
            if (document is not null) documents.Add(document);
        }

        Directory.Move(current.DirectoryPath, destinationDirectory);
        await WriteProjectMarkerAsync(
            Path.Combine(destinationDirectory, ProjectMarker),
            newId,
            current.IsPinned,
            current.SortOrder,
            cancellationToken);

        foreach (var document in documents)
        {
            await SaveAsync(document with { ProjectId = newId, UpdatedAt = DateTimeOffset.Now }, cancellationToken);
        }

        return new WorkspaceProject(newId, newId, false, destinationDirectory, current.IsPinned, current.SortOrder);
    }

    internal async Task<WorkspaceProject> SetProjectPinnedAsync(
        WorkspaceProject project,
        bool isPinned,
        CancellationToken cancellationToken = default)
    {
        var current = await GetCustomProjectAsync(project.Id, cancellationToken);
        await WriteProjectMarkerAsync(
            Path.Combine(current.DirectoryPath, ProjectMarker),
            current.Name,
            isPinned,
            current.SortOrder,
            cancellationToken);
        return current with { IsPinned = isPinned };
    }

    internal async Task ReorderProjectAsync(
        WorkspaceProject source,
        WorkspaceProject target,
        CancellationToken cancellationToken = default)
    {
        var currentProjects = await GetProjectsAsync(cancellationToken);
        var currentSource = currentProjects.SingleOrDefault(project => project.Id == source.Id)
            ?? throw new InvalidDataException("源项目不存在。");
        var currentTarget = currentProjects.SingleOrDefault(project => project.Id == target.Id)
            ?? throw new InvalidDataException("目标项目不存在。");
        if (currentSource.IsSystem || currentTarget.IsSystem)
        {
            throw new InvalidOperationException("系统项目位置已锁定。");
        }
        if (currentSource.IsPinned != currentTarget.IsPinned)
        {
            throw new InvalidOperationException("置顶项目与普通项目请在各自分组内排序。");
        }

        var group = currentProjects
            .Where(project => !project.IsSystem && project.IsPinned == currentSource.IsPinned)
            .OrderBy(project => project.SortOrder)
            .ThenBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var sourceIndex = group.FindIndex(project => project.Id == currentSource.Id);
        var targetIndex = group.FindIndex(project => project.Id == currentTarget.Id);
        if (sourceIndex == targetIndex) return;
        group.RemoveAt(sourceIndex);
        group.Insert(targetIndex, currentSource);

        for (var index = 0; index < group.Count; index++)
        {
            var project = group[index];
            await WriteProjectMarkerAsync(
                Path.Combine(project.DirectoryPath, ProjectMarker),
                project.Name,
                project.IsPinned,
                index,
                cancellationToken);
        }
    }

    internal async Task DeleteProjectAsync(
        WorkspaceProject project,
        CancellationToken cancellationToken = default)
    {
        var current = await GetCustomProjectAsync(project.Id, cancellationToken);
        if (Directory.EnumerateFiles(current.DirectoryPath, "conversation-*.md").Any())
        {
            throw new InvalidOperationException("项目中仍有会话。请先移走或删除这些会话。");
        }

        var markerPath = Path.Combine(current.DirectoryPath, ProjectMarker);
        if (File.Exists(markerPath)) File.Delete(markerPath);
        Directory.Delete(current.DirectoryPath, false);
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

    internal async Task<ConversationDocument?> FindByCopilotConversationUrlAsync(
        string conversationUrl,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootDirectory)) return null;
        foreach (var path in Directory.EnumerateFiles(_rootDirectory, "conversation-*.md", SearchOption.AllDirectories))
        {
            var document = await TryLoadPathAsync(path, cancellationToken);
            if (string.Equals(document?.CopilotConversationUrl, conversationUrl, StringComparison.OrdinalIgnoreCase))
            {
                return document;
            }
        }
        return null;
    }

    internal async Task<ConversationDocument> ImportHistoricalConversationAsync(
        HistoricalConversationSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (await FindByCopilotConversationUrlAsync(snapshot.ConversationUrl, cancellationToken) is not null)
        {
            throw new InvalidOperationException("This Copilot conversation has already been imported.");
        }

        var importedAt = DateTimeOffset.Now;
        var document = new ConversationDocument
        {
            ProjectId = StandaloneProjectId,
            CopilotConversationUrl = snapshot.ConversationUrl,
            CopilotConversationId = ExtractConversationId(snapshot.ConversationUrl),
            CopilotTitleInitial = NormalizeTitle(snapshot.CopilotTitle),
            CopilotTitleCurrent = NormalizeTitle(snapshot.CopilotTitle),
            TitleSource = "copilot_import",
            Mode = "history_import",
            CreatedAt = importedAt,
            UpdatedAt = importedAt,
            Turns = snapshot.Turns.Select(turn => new ConversationTurn(
                importedAt,
                turn.Role,
                turn.Markdown,
                null,
                turn.Role == "copilot" ? "unknown" : "not_applicable")).ToArray()
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

    internal Task DeleteConversationAsync(ConversationDocument document)
    {
        var path = FindPath(document.Id);
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException("本地会话文件不存在。", path);
        }

        File.Delete(path);
        return Task.CompletedTask;
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

    internal string RenderForDisplay(ConversationDocument document)
    {
        var markdown = Render(document);
        if (!markdown.StartsWith(MetadataPrefix, StringComparison.Ordinal)) return markdown;

        var metadataEnd = markdown.IndexOf("-->", MetadataPrefix.Length, StringComparison.Ordinal);
        if (metadataEnd < 0) return markdown;

        return markdown[(metadataEnd + 3)..].TrimStart('\r', '\n');
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
            await WriteProjectMarkerAsync(marker, name, false, int.MaxValue, cancellationToken);
        }
        var metadata = isSystem
            ? new ProjectMarkerMetadata(false, int.MaxValue)
            : await ReadProjectMarkerAsync(marker, cancellationToken);
        return new WorkspaceProject(
            id,
            name,
            isSystem,
            path,
            metadata.IsPinned,
            NormalizeSortOrder(metadata.SortOrder));
    }

    private async Task EnsureKnownProjectAsync(string projectId, CancellationToken cancellationToken)
    {
        var projects = await GetProjectsAsync(cancellationToken);
        if (projects.All(project => !project.Id.Equals(projectId, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("目标项目不存在。");
        }
    }

    private async Task<WorkspaceProject> GetCustomProjectAsync(string projectId, CancellationToken cancellationToken)
    {
        var project = (await GetProjectsAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.Id.Equals(projectId, StringComparison.Ordinal));
        if (project is null) throw new InvalidDataException("目标项目不存在。");
        if (project.IsSystem) throw new InvalidOperationException("系统项目不能重命名或删除。");
        return project;
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

    private static async Task<ProjectMarkerMetadata> ReadProjectMarkerAsync(
        string markerPath,
        CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(markerPath, cancellationToken);
        var end = text.IndexOf(" -->", StringComparison.Ordinal);
        if (!text.StartsWith(ProjectMetadataPrefix, StringComparison.Ordinal) || end < 0)
        {
            return new ProjectMarkerMetadata(false, int.MaxValue);
        }

        try
        {
            var encoded = text[ProjectMetadataPrefix.Length..end];
            return JsonSerializer.Deserialize<ProjectMarkerMetadata>(Convert.FromBase64String(encoded), JsonOptions)
                   ?? new ProjectMarkerMetadata(false, int.MaxValue);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return new ProjectMarkerMetadata(false, int.MaxValue);
        }
    }

    private static async Task WriteProjectMarkerAsync(
        string markerPath,
        string name,
        bool isPinned,
        int sortOrder,
        CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
            new ProjectMarkerMetadata(isPinned, sortOrder), JsonOptions));
        var temporaryPath = markerPath + ".tmp";
        var contents = $"{ProjectMetadataPrefix}{encoded} -->\n\n# {name}\n\nCopilot Bridge 项目目录。\n";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, contents, new UTF8Encoding(false), cancellationToken);
            File.Move(temporaryPath, markerPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
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

    private async Task<int> GetNextProjectSortOrderAsync(CancellationToken cancellationToken)
    {
        var maximum = -1;
        foreach (var directory in Directory.EnumerateDirectories(_rootDirectory))
        {
            var name = Path.GetFileName(directory);
            if (name is LegacyInboxProjectId or StandaloneProjectId or LegacyStandaloneProjectId) continue;
            var markerPath = Path.Combine(directory, ProjectMarker);
            if (!File.Exists(markerPath)) continue;
            var metadata = await ReadProjectMarkerAsync(markerPath, cancellationToken);
            var order = NormalizeSortOrder(metadata.SortOrder);
            if (order != int.MaxValue) maximum = Math.Max(maximum, order);
        }
        return maximum + 1;
    }

    private async Task MigrateSystemProjectsAsync(CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.Combine(_rootDirectory, StandaloneProjectId);
        Directory.CreateDirectory(destinationDirectory);
        foreach (var legacyProjectId in new[] { LegacyStandaloneProjectId, LegacyInboxProjectId })
        {
            var legacyDirectory = Path.Combine(_rootDirectory, legacyProjectId);
            if (!Directory.Exists(legacyDirectory)) continue;

            foreach (var sourcePath in Directory.EnumerateFiles(legacyDirectory, "conversation-*.md"))
            {
                var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));
                var document = await TryLoadPathAsync(sourcePath, cancellationToken);
                if (document is null)
                {
                    if (File.Exists(destinationPath))
                    {
                        destinationPath = Path.Combine(
                            destinationDirectory,
                            $"legacy-{legacyProjectId}-{Path.GetFileName(sourcePath)}");
                    }
                    File.Move(sourcePath, destinationPath, true);
                    continue;
                }

                var updated = document with { ProjectId = StandaloneProjectId };
                var temporaryPath = destinationPath + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(temporaryPath, Render(updated), new UTF8Encoding(false), cancellationToken);
                    File.Move(temporaryPath, destinationPath, true);
                }
                finally
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                if (File.Exists(sourcePath)) File.Delete(sourcePath);
            }
            foreach (var path in Directory.EnumerateFiles(legacyDirectory))
            {
                if (File.Exists(path)) File.Delete(path);
            }
            Directory.Delete(legacyDirectory, false);
        }

        foreach (var path in Directory.EnumerateFiles(destinationDirectory, "conversation-*.md"))
        {
            var document = await TryLoadPathAsync(path, cancellationToken);
            if (document is null || document.ProjectId is not (LegacyStandaloneProjectId or LegacyInboxProjectId)) continue;
            var updated = document with { ProjectId = StandaloneProjectId };
            var temporaryPath = path + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporaryPath, Render(updated), new UTF8Encoding(false), cancellationToken);
                File.Move(temporaryPath, path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
        var markerPath = Path.Combine(destinationDirectory, ProjectMarker);
        if (!File.Exists(markerPath))
        {
            await WriteProjectMarkerAsync(
                markerPath,
                StandaloneProjectId,
                false,
                int.MaxValue,
                cancellationToken);
        }
    }

    private static int SystemProjectOrder(string projectId) => projectId == StandaloneProjectId ? 0 : 1;

    private static int NormalizeSortOrder(int sortOrder) => sortOrder < 0 ? int.MaxValue : sortOrder;

    private sealed record ProjectMarkerMetadata(bool IsPinned, int SortOrder = int.MaxValue);

    private static string? ExtractConversationId(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 3 && segments[^2].Equals("conversation", StringComparison.OrdinalIgnoreCase)
            ? segments[^1]
            : null;
    }

    private static string NormalizeTitle(string? value)
    {
        var normalized = string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(normalized) ? "未命名 Copilot 对话" : normalized;
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
