using System.Text;
using System.Text.Json;
using CopilotBridge.Browser;

namespace CopilotBridge.Core;

internal enum ConversationAccessLevel
{
    Off,
    Metadata,
    Snippets,
    Full
}

internal sealed record WorkspaceProject(
    string Id,
    string Name,
    bool IsSystem,
    string DirectoryPath,
    bool IsPinned = false,
    int SortOrder = int.MaxValue,
    ConversationAccessLevel AccessLevel = ConversationAccessLevel.Off);

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

internal sealed record WorkspaceConversationMatch(
    string ConversationId,
    string ProjectId,
    string DisplayTitle,
    string CopilotTitle,
    DateTimeOffset UpdatedAt,
    string Mode,
    string? LastModel,
    int TurnCount,
    ConversationAccessLevel AccessLevel,
    string MatchScope,
    string? MatchRole,
    string? Snippet);

internal sealed record WorkspaceConversationPage(
    string ConversationId,
    string ProjectId,
    string DisplayTitle,
    string CopilotTitle,
    string? CopilotConversationUrl,
    DateTimeOffset UpdatedAt,
    string Mode,
    int StartTurn,
    int TotalTurns,
    bool HasMore,
    IReadOnlyList<ConversationTurn> Turns);

internal sealed record ConversationStorageMigrationPreview(int LegacyCount, int V2Count);
internal sealed record ConversationStorageMigrationResult(int MigratedCount, string? BackupDirectory);

internal sealed class WorkspaceAccessException(string errorCode) : InvalidOperationException(errorCode)
{
    internal string ErrorCode { get; } = errorCode;
}

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
    private readonly ConversationStorageV2 _storageV2;

    internal ConversationWorkspaceStore(string? rootDirectory = null)
    {
        _rootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopilotBridge",
            "workspace");
        _storageV2 = new ConversationStorageV2(_rootDirectory);
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
                NormalizeSortOrder(metadata.SortOrder),
                metadata.AccessLevel));
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
            project.AccessLevel,
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
            current.AccessLevel,
            cancellationToken);

        foreach (var document in documents)
        {
            await SaveAsync(document with { ProjectId = newId, UpdatedAt = DateTimeOffset.Now }, cancellationToken);
        }

        return new WorkspaceProject(
            newId,
            newId,
            false,
            destinationDirectory,
            current.IsPinned,
            current.SortOrder,
            current.AccessLevel);
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
            current.AccessLevel,
            cancellationToken);
        return current with { IsPinned = isPinned };
    }

    internal async Task<WorkspaceProject> SetProjectAccessAsync(
        WorkspaceProject project,
        ConversationAccessLevel accessLevel,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(accessLevel))
        {
            throw new InvalidDataException("项目访问级别无效。");
        }

        var current = (await GetProjectsAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.Id.Equals(project.Id, StringComparison.Ordinal))
            ?? throw new InvalidDataException("目标项目不存在。");
        await WriteProjectMarkerAsync(
            Path.Combine(current.DirectoryPath, ProjectMarker),
            current.Name,
            current.IsPinned,
            current.SortOrder,
            accessLevel,
            cancellationToken);
        return current with { AccessLevel = accessLevel };
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
                project.AccessLevel,
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
        foreach (var path in EnumerateConversationFiles())
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
        var documents = await GetConversationDocumentsAsync(projectId, cancellationToken);
        return documents
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

    internal async Task<IReadOnlyList<ConversationDocument>> GetConversationDocumentsAsync(
        string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var documents = new List<ConversationDocument>();
        foreach (var path in EnumerateConversationFiles())
        {
            if (Path.GetFileName(path).Equals(ProjectMarker, StringComparison.OrdinalIgnoreCase)) continue;
            var document = await TryLoadPathAsync(path, cancellationToken);
            if (document is not null && (projectId is null || document.ProjectId == projectId))
            {
                documents.Add(document);
            }
        }

        return documents
            .OrderByDescending(document => document.UpdatedAt)
            .ToArray();
    }

    internal async Task<ConversationDocument?> FindAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        var path = await FindPathAsync(conversationId, cancellationToken).ConfigureAwait(false);
        return path is null ? null : await TryLoadPathAsync(path, cancellationToken);
    }

    internal async Task<IReadOnlyList<WorkspaceConversationMatch>> SearchAuthorizedConversationsAsync(
        string? query = null,
        string? projectId = null,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "检索数量必须在 1–20 之间。");
        }

        var projects = (await GetProjectsForReadAsync(cancellationToken))
            .Where(project => project.AccessLevel != ConversationAccessLevel.Off)
            .ToArray();
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            var selected = projects.FirstOrDefault(project =>
                project.Id.Equals(projectId.Trim(), StringComparison.Ordinal));
            if (selected is null) throw new WorkspaceAccessException("project_not_accessible");
            projects = [selected];
        }

        var accessByProject = projects.ToDictionary(
            project => project.Id,
            project => project.AccessLevel,
            StringComparer.Ordinal);
        if (accessByProject.Count == 0) return [];
        var normalizedQuery = query?.Trim() ?? string.Empty;
        var matches = new List<WorkspaceConversationMatch>();
        foreach (var path in EnumerateConversationFiles())
        {
            if (Path.GetFileName(path).Equals(ProjectMarker, StringComparison.OrdinalIgnoreCase)) continue;
            var document = await TryLoadPathAsync(path, cancellationToken);
            if (document is null || !accessByProject.TryGetValue(document.ProjectId, out var accessLevel)) continue;

            var lastModel = document.Turns.LastOrDefault(turn => !string.IsNullOrWhiteSpace(turn.Model))?.Model;
            var metadata = string.Join('\n',
                document.DisplayTitle,
                document.CopilotTitleInitial,
                document.CopilotTitleCurrent,
                document.Mode,
                lastModel ?? string.Empty);
            var metadataMatch = normalizedQuery.Length == 0 ||
                                metadata.Contains(normalizedQuery, StringComparison.CurrentCultureIgnoreCase);
            var contentMatch = metadataMatch || accessLevel == ConversationAccessLevel.Metadata
                ? null
                : document.Turns.FirstOrDefault(turn =>
                    turn.Markdown.Contains(normalizedQuery, StringComparison.CurrentCultureIgnoreCase));
            if (!metadataMatch && contentMatch is null) continue;

            matches.Add(new WorkspaceConversationMatch(
                document.Id,
                document.ProjectId,
                document.DisplayTitle,
                document.CopilotTitleCurrent,
                document.UpdatedAt,
                document.Mode,
                lastModel,
                document.Turns.Count,
                accessLevel,
                metadataMatch ? "metadata" : "content",
                contentMatch?.Role,
                contentMatch is null ? null : BuildSnippet(contentMatch.Markdown, normalizedQuery)));
        }

        return matches
            .OrderBy(match => match.MatchScope == "metadata" ? 0 : 1)
            .ThenByDescending(match => match.UpdatedAt)
            .ThenBy(match => match.ConversationId, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    internal async Task<WorkspaceConversationPage> ReadAuthorizedConversationAsync(
        string conversationId,
        int startTurn = 0,
        int maxTurns = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            throw new WorkspaceAccessException("conversation_not_accessible");
        }
        if (startTurn < 0) throw new ArgumentOutOfRangeException(nameof(startTurn));
        if (maxTurns is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTurns), "读取轮次必须在 1–20 之间。");
        }

        var document = await FindAsync(conversationId.Trim(), cancellationToken);
        if (document is null) throw new WorkspaceAccessException("conversation_not_accessible");
        var project = (await GetProjectsForReadAsync(cancellationToken))
            .FirstOrDefault(candidate => candidate.Id.Equals(document.ProjectId, StringComparison.Ordinal));
        if (project?.AccessLevel != ConversationAccessLevel.Full)
        {
            throw new WorkspaceAccessException("conversation_not_accessible");
        }

        var turns = document.Turns.Skip(startTurn).Take(maxTurns).ToArray();
        return new WorkspaceConversationPage(
            document.Id,
            document.ProjectId,
            document.DisplayTitle,
            document.CopilotTitleCurrent,
            document.CopilotConversationUrl,
            document.UpdatedAt,
            document.Mode,
            startTurn,
            document.Turns.Count,
            startTurn + turns.Length < document.Turns.Count,
            turns);
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
        var oldPath = await FindPathAsync(document.Id, cancellationToken);
        document = document with { ProjectId = destinationProjectId, UpdatedAt = DateTimeOffset.Now };
        await SaveAsync(document, cancellationToken);
        if (oldPath is not null && File.Exists(oldPath) &&
            !oldPath.Equals(PathFor(document), StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(oldPath);
        }

        return document;
    }

    internal async Task DeleteConversationAsync(ConversationDocument document)
    {
        var path = await FindPathAsync(document.Id);
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException("本地会话文件不存在。", path);
        }

        File.Delete(path);
        await _storageV2.DeleteAsync(document.Id);
    }

    internal async Task<ConversationDocument> AppendRunAsync(
        ConversationDocument document,
        CollaborationRunResult result,
        CancellationToken cancellationToken = default,
        string? collaborationMode = null)
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
            Mode = ResolveAppendMode(document.Mode, collaborationMode),
            Turns = turns,
            UpdatedAt = DateTimeOffset.Now
        };
        await SaveAsync(document, cancellationToken);
        return document;
    }

    internal async Task<ConversationDocument> AppendIncompleteDeliveryAsync(
        ConversationDocument document,
        string requestMarkdown,
        string status,
        string? reviewer = null,
        string? conversationUrl = null,
        CancellationToken cancellationToken = default,
        string? collaborationMode = null)
    {
        var turns = document.Turns.ToList();
        turns.Add(new ConversationTurn(
            DateTimeOffset.Now,
            "agent",
            requestMarkdown,
            null,
            status,
            reviewer));
        document = document with
        {
            CopilotConversationUrl = conversationUrl ?? document.CopilotConversationUrl,
            CopilotConversationId = ExtractConversationId(conversationUrl) ?? document.CopilotConversationId,
            Mode = ResolveAppendMode(document.Mode, collaborationMode),
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

    internal async Task<ConversationStorageMigrationPreview> GetStorageMigrationPreviewAsync(
        CancellationToken cancellationToken = default)
    {
        var legacy = 0;
        var v2 = 0;
        foreach (var path in EnumerateConversationFiles())
        {
            var document = await TryLoadPathAsync(path, cancellationToken);
            if (document is null) continue;
            if (_storageV2.HasSidecar(document.Id)) v2++;
            else legacy++;
        }
        return new ConversationStorageMigrationPreview(legacy, v2);
    }

    internal async Task<ConversationStorageMigrationResult> MigrateStorageV2Async(
        CancellationToken cancellationToken = default)
    {
        var legacy = new List<(string Path, ConversationDocument Document)>();
        foreach (var path in EnumerateConversationFiles())
        {
            var document = await TryLoadPathAsync(path, cancellationToken);
            if (document is not null && !_storageV2.HasSidecar(document.Id)) legacy.Add((path, document));
        }
        if (legacy.Count == 0) return new ConversationStorageMigrationResult(0, null);

        _storageV2.EnsureManagedDirectories();
        Directory.CreateDirectory(_storageV2.BackupsDirectory);
        var backupDirectory = Path.Combine(
            _storageV2.BackupsDirectory,
            $"v1-{DateTimeOffset.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDirectory);
        var entries = new List<StorageMigrationEntry>();
        foreach (var item in legacy)
        {
            var relative = Path.GetRelativePath(_rootDirectory, item.Path).Replace('\\', '/');
            var backupPath = Path.Combine(backupDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(item.Path, backupPath, true);
            var original = await File.ReadAllTextAsync(item.Path, cancellationToken);
            entries.Add(new StorageMigrationEntry(item.Document.Id, relative, ConversationStorageV2.Sha256(original), null));
        }

        var manifestPath = Path.Combine(backupDirectory, "manifest.json");
        var manifest = new StorageMigrationManifest("prepared", DateTimeOffset.Now, entries);
        await WriteMigrationManifestAsync(manifestPath, manifest, cancellationToken);
        try
        {
            for (var index = 0; index < legacy.Count; index++)
            {
                var item = legacy[index];
                await SaveAsync(item.Document, cancellationToken);
                var migrated = await File.ReadAllTextAsync(PathFor(item.Document), cancellationToken);
                entries[index] = entries[index] with { MigratedSha256 = ConversationStorageV2.Sha256(migrated) };
            }
            manifest = manifest with { Status = "completed", Entries = entries };
            await WriteMigrationManifestAsync(manifestPath, manifest, cancellationToken);
            return new ConversationStorageMigrationResult(legacy.Count, backupDirectory);
        }
        catch
        {
            foreach (var entry in entries)
            {
                var backupPath = Path.Combine(backupDirectory, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var targetPath = Path.Combine(_rootDirectory, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(backupPath, targetPath, true);
                await _storageV2.DeleteAsync(entry.ConversationId, cancellationToken);
            }
            await WriteMigrationManifestAsync(
                manifestPath,
                manifest with { Status = "rolled_back_after_failure" },
                cancellationToken);
            throw;
        }
    }

    internal async Task<ConversationStorageMigrationResult> RollbackLatestStorageMigrationAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_storageV2.BackupsDirectory))
        {
            return new ConversationStorageMigrationResult(0, null);
        }
        foreach (var directory in Directory.EnumerateDirectories(_storageV2.BackupsDirectory, "v1-*")
                     .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            StorageMigrationManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<StorageMigrationManifest>(
                    await File.ReadAllTextAsync(manifestPath, cancellationToken), JsonOptions);
            }
            catch (JsonException) { continue; }
            if (manifest?.Status != "completed") continue;

            foreach (var entry in manifest.Entries)
            {
                var currentPath = await FindPathAsync(entry.ConversationId, cancellationToken);
                var expectedPath = Path.GetFullPath(Path.Combine(
                    _rootDirectory,
                    entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (currentPath is null || !Path.GetFullPath(currentPath).Equals(
                        expectedPath, StringComparison.OrdinalIgnoreCase) || entry.MigratedSha256 is null)
                {
                    throw new InvalidOperationException("迁移后会话路径已变化，不能自动回滚。");
                }
                var current = await File.ReadAllTextAsync(currentPath, cancellationToken);
                if (!ConversationStorageV2.Sha256(current).Equals(
                        entry.MigratedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("迁移后会话已修改，不能覆盖为旧备份。");
                }
            }

            foreach (var entry in manifest.Entries)
            {
                var backupPath = Path.Combine(directory, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var targetPath = Path.Combine(_rootDirectory, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                File.Copy(backupPath, targetPath, true);
                await _storageV2.DeleteAsync(entry.ConversationId, cancellationToken);
            }
            await WriteMigrationManifestAsync(
                manifestPath,
                manifest with { Status = "rolled_back" },
                cancellationToken);
            return new ConversationStorageMigrationResult(manifest.Entries.Count, directory);
        }
        return new ConversationStorageMigrationResult(0, null);
    }

    internal async Task SaveAsync(ConversationDocument document, CancellationToken cancellationToken = default)
    {
        await EnsureKnownProjectAsync(document.ProjectId, cancellationToken);
        var path = PathFor(document);
        await _storageV2.SaveAsync(document, path, cancellationToken);
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
        return _storageV2.RenderReadable(document);
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
            await WriteProjectMarkerAsync(
                marker,
                name,
                false,
                int.MaxValue,
                ConversationAccessLevel.Off,
                cancellationToken);
        }
        var storedMetadata = await ReadProjectMarkerAsync(marker, cancellationToken);
        var metadata = isSystem
            ? storedMetadata with { IsPinned = false, SortOrder = int.MaxValue }
            : storedMetadata;
        return new WorkspaceProject(
            id,
            name,
            isSystem,
            path,
            metadata.IsPinned,
            NormalizeSortOrder(metadata.SortOrder),
            metadata.AccessLevel);
    }

    private async Task EnsureKnownProjectAsync(string projectId, CancellationToken cancellationToken)
    {
        var projects = await GetProjectsAsync(cancellationToken);
        if (projects.All(project => !project.Id.Equals(projectId, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("目标项目不存在。");
        }
    }

    private async Task<IReadOnlyList<WorkspaceProject>> GetProjectsForReadAsync(
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_rootDirectory)) return [];
        var projects = new List<WorkspaceProject>();
        foreach (var directory in Directory.EnumerateDirectories(_rootDirectory))
        {
            var name = Path.GetFileName(directory);
            if (name is LegacyInboxProjectId or LegacyStandaloneProjectId) continue;
            var markerPath = Path.Combine(directory, ProjectMarker);
            var isSystem = name == StandaloneProjectId;
            if (!isSystem && !File.Exists(markerPath)) continue;
            var metadata = File.Exists(markerPath)
                ? await ReadProjectMarkerAsync(markerPath, cancellationToken)
                : new ProjectMarkerMetadata(false, int.MaxValue, ConversationAccessLevel.Off);
            projects.Add(new WorkspaceProject(
                name,
                name,
                isSystem,
                directory,
                metadata.IsPinned,
                NormalizeSortOrder(metadata.SortOrder),
                metadata.AccessLevel));
        }
        return projects;
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

    private async Task<string?> FindPathAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootDirectory)) return null;
        var relative = await _storageV2.RelativeMarkdownPathAsync(conversationId, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(relative))
        {
            var sidecarPath = Path.GetFullPath(Path.Combine(
                _rootDirectory,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(sidecarPath)) return sidecarPath;
        }
        return await Task.Run(
                () => EnumerateConversationFiles()
                    .FirstOrDefault(path => Path.GetFileName(path).Equals(
                        $"conversation-{conversationId}.md", StringComparison.OrdinalIgnoreCase)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ConversationDocument?> TryLoadPathAsync(string path, CancellationToken cancellationToken)
    {
        var v2 = await _storageV2.TryLoadAsync(path, cancellationToken);
        if (v2 is not null) return v2;
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
            return new ProjectMarkerMetadata(false, int.MaxValue, ConversationAccessLevel.Off);
        }

        try
        {
            var encoded = text[ProjectMetadataPrefix.Length..end];
            var metadata = JsonSerializer.Deserialize<ProjectMarkerMetadata>(
                               Convert.FromBase64String(encoded),
                               JsonOptions)
                           ?? new ProjectMarkerMetadata(false, int.MaxValue, ConversationAccessLevel.Off);
            return metadata with { AccessLevel = NormalizeAccessLevel(metadata.AccessLevel) };
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return new ProjectMarkerMetadata(false, int.MaxValue, ConversationAccessLevel.Off);
        }
    }

    private IEnumerable<string> EnumerateConversationFiles()
    {
        if (!Directory.Exists(_rootDirectory)) return [];
        var managedRoot = Path.GetFullPath(_storageV2.BridgeDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(_rootDirectory, "conversation-*.md", SearchOption.AllDirectories)
            .Where(path => !Path.GetFullPath(path).StartsWith(managedRoot, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WriteProjectMarkerAsync(
        string markerPath,
        string name,
        bool isPinned,
        int sortOrder,
        ConversationAccessLevel accessLevel,
        CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
            new ProjectMarkerMetadata(isPinned, sortOrder, accessLevel), JsonOptions));
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
                ConversationAccessLevel.Off,
                cancellationToken);
        }
    }

    private static int SystemProjectOrder(string projectId) => projectId == StandaloneProjectId ? 0 : 1;

    private static int NormalizeSortOrder(int sortOrder) => sortOrder < 0 ? int.MaxValue : sortOrder;

    private static ConversationAccessLevel NormalizeAccessLevel(ConversationAccessLevel accessLevel) =>
        Enum.IsDefined(accessLevel) ? accessLevel : ConversationAccessLevel.Off;

    private static string ResolveAppendMode(string currentMode, string? collaborationMode)
    {
        if (string.IsNullOrWhiteSpace(collaborationMode)) return currentMode;
        return collaborationMode.Trim().ToLowerInvariant() switch
        {
            "assist" => "assist",
            "outsource" => "outsource",
            "review" => "review",
            _ => throw new InvalidDataException("协作模式无效。")
        };
    }

    private static async Task WriteMigrationManifestAsync(
        string path,
        StorageMigrationManifest manifest,
        CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(manifest, JsonOptions),
                new UTF8Encoding(false),
                cancellationToken);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed record ProjectMarkerMetadata(
        bool IsPinned,
        int SortOrder = int.MaxValue,
        ConversationAccessLevel AccessLevel = ConversationAccessLevel.Off);

    private sealed record StorageMigrationManifest(
        string Status,
        DateTimeOffset CreatedAt,
        IReadOnlyList<StorageMigrationEntry> Entries);

    private sealed record StorageMigrationEntry(
        string ConversationId,
        string RelativePath,
        string OriginalSha256,
        string? MigratedSha256);

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
