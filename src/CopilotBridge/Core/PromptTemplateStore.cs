using System.Text.Json;

namespace CopilotBridge.Core;

internal sealed record PromptTemplate(string Id, string Name, string Content);

internal sealed class PromptTemplateStore
{
    private const int MaximumNameLength = 80;
    private const int MaximumContentLength = 20_000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    internal PromptTemplateStore(string? path = null)
    {
        FilePath = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopilotBridge",
            "prompt-templates.json");
    }

    internal string FilePath { get; }

    internal async Task<IReadOnlyList<PromptTemplate>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath)) return [];
        try
        {
            var json = await File.ReadAllTextAsync(FilePath, cancellationToken);
            var templates = JsonSerializer.Deserialize<PromptTemplate[]>(json, JsonOptions) ?? [];
            return templates.Select(Validate)
                .OrderBy(template => template.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("提示模板文件格式无效。", exception);
        }
    }

    internal async Task<PromptTemplate> SaveAsync(
        string? id,
        string name,
        string content,
        CancellationToken cancellationToken = default)
    {
        var template = Validate(new PromptTemplate(
            string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
            name,
            content));
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var templates = (await LoadAsync(cancellationToken)).ToList();
            var duplicate = templates.FirstOrDefault(candidate =>
                candidate.Name.Equals(template.Name, StringComparison.CurrentCultureIgnoreCase) &&
                !candidate.Id.Equals(template.Id, StringComparison.Ordinal));
            if (duplicate is not null) throw new InvalidDataException("提示模板名称已存在。");
            templates.RemoveAll(candidate => candidate.Id.Equals(template.Id, StringComparison.Ordinal));
            templates.Add(template);
            await WriteAsync(templates, cancellationToken);
            return template;
        }
        finally { _writeLock.Release(); }
    }

    internal async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var templates = (await LoadAsync(cancellationToken))
                .Where(candidate => !candidate.Id.Equals(id, StringComparison.Ordinal))
                .ToArray();
            await WriteAsync(templates, cancellationToken);
        }
        finally { _writeLock.Release(); }
    }

    private async Task WriteAsync(
        IEnumerable<PromptTemplate> templates,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("提示模板路径没有父目录。");
        Directory.CreateDirectory(directory);
        var temporary = FilePath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(
                    templates.OrderBy(template => template.Name, StringComparer.CurrentCultureIgnoreCase),
                    JsonOptions),
                cancellationToken);
            File.Move(temporary, FilePath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static PromptTemplate Validate(PromptTemplate template)
    {
        var id = template.Id?.Trim() ?? string.Empty;
        var name = template.Name?.Trim() ?? string.Empty;
        var content = template.Content?.Trim() ?? string.Empty;
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("提示模板 ID 无效。");
        if (name.Length is 0 or > MaximumNameLength)
        {
            throw new InvalidDataException($"提示模板名称必须为 1–{MaximumNameLength} 个字符。");
        }
        if (content.Length is 0 or > MaximumContentLength)
        {
            throw new InvalidDataException($"提示模板内容必须为 1–{MaximumContentLength} 个字符。");
        }
        return template with { Id = id, Name = name, Content = content };
    }
}
