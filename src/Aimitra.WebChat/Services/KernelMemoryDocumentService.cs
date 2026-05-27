using System.Text.Json;
using Microsoft.KernelMemory;

namespace Aimitra.WebChat.Services;

public sealed class KernelMemoryDocumentService : IDocumentMemoryService
{
    private readonly IKernelMemory _memory;
    private readonly string _baseFolder;
    private readonly string _statePath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();

    public KernelMemoryDocumentService(IKernelMemory memory)
    {
        _memory = memory;
        _baseFolder = Path.Combine(AppContext.BaseDirectory, "App_Data", "knowledge-base-km");
        _statePath = Path.Combine(_baseFolder, "state.json");
        Directory.CreateDirectory(_baseFolder);
    }

    public async Task<string> ImportDocumentAsync(string filePath, string? collection = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("File path is required.", nameof(filePath));
        if (!File.Exists(filePath)) throw new FileNotFoundException("Document not found.", filePath);

        var collectionName = GetCollectionName(collection);
        var storedPath = CopyIntoCollectionFolder(filePath, collectionName);
        var searchTextPath = Path.ChangeExtension(storedPath, ".txt");
        var extractedText = ExtractSearchText(filePath);
        if (!string.IsNullOrWhiteSpace(extractedText))
        {
            await File.WriteAllTextAsync(searchTextPath, extractedText, cancellationToken).ConfigureAwait(false);
        }

        var id = Guid.NewGuid().ToString("N");
        var tags = new TagCollection();
        await _memory.ImportDocumentAsync(storedPath, collectionName, tags, id, Array.Empty<string>(), cancellationToken).ConfigureAwait(false);

        var entry = new DocumentMemoryEntry(
            Id: id,
            Title: Path.GetFileNameWithoutExtension(filePath),
            FileName: Path.GetFileName(filePath),
            Collection: collectionName,
            StoredPath: storedPath,
            SearchTextPath: searchTextPath,
            ContentType: GetContentType(filePath),
            ImportedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        SaveEntry(entry);
        SetLastCollection(collectionName);
        return collectionName;
    }

    public async Task<string> ImportTextAsync(string title, string content, string? collection = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Content is required.", nameof(content));

        var collectionName = GetCollectionName(collection);
        var id = Guid.NewGuid().ToString("N");
        var storedPath = Path.Combine(GetCollectionFolder(collectionName), $"{id}.txt");
        await File.WriteAllTextAsync(storedPath, content, cancellationToken).ConfigureAwait(false);

        var tags = new TagCollection();
        await _memory.ImportTextAsync(title, content, tags, id, Array.Empty<string>(), cancellationToken).ConfigureAwait(false);

        var entry = new DocumentMemoryEntry(
            Id: id,
            Title: string.IsNullOrWhiteSpace(title) ? id : title,
            FileName: $"{id}.txt",
            Collection: collectionName,
            StoredPath: storedPath,
            SearchTextPath: storedPath,
            ContentType: "text/plain",
            ImportedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

        SaveEntry(entry);
        SetLastCollection(collectionName);
        return collectionName;
    }

    public async Task<IReadOnlyList<DocumentMemoryMatch>> AskAsync(string question, string? collection = null, int topK = 5, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question)) throw new ArgumentException("Question is required.", nameof(question));

        var collectionName = GetCollectionName(collection);
        SetLastCollection(collectionName);
        var answer = await _memory.AskAsync(question, collectionName, null, null, 0.0, cancellationToken).ConfigureAwait(false);

        var source = answer.RelevantSources?.FirstOrDefault() is { } citation
            ? citation.SourceName ?? citation.DocumentId ?? collectionName
            : collectionName;
        var snippet = string.IsNullOrWhiteSpace(answer.Result) ? answer.NoResultReason ?? string.Empty : answer.Result;

        return new[]
        {
            new DocumentMemoryMatch(
                Source: source,
                Title: collectionName,
                Snippet: snippet.Length > 500 ? snippet[..500] + "..." : snippet,
                Score: null)
        };
    }

    public Task<IReadOnlyList<DocumentMemoryEntry>> ListDocumentsAsync(string? collection = null, CancellationToken cancellationToken = default)
    {
        var entries = LoadEntries(GetCollectionName(collection))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToList();
        return Task.FromResult<IReadOnlyList<DocumentMemoryEntry>>(entries);
    }

    public async Task DeleteDocumentAsync(string documentId, string? collection = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId)) throw new ArgumentException("Document id is required.", nameof(documentId));

        var collectionName = GetCollectionName(collection);
        await _memory.DeleteDocumentAsync(collectionName, documentId, cancellationToken).ConfigureAwait(false);
        RemoveEntry(collectionName, documentId);
    }

    public async Task ReindexDocumentAsync(string documentId, string? collection = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId)) throw new ArgumentException("Document id is required.", nameof(documentId));

        var collectionName = GetCollectionName(collection);
        var entry = LoadEntries(collectionName).FirstOrDefault(x => string.Equals(x.Id, documentId, StringComparison.OrdinalIgnoreCase));
        if (entry is null || !File.Exists(entry.StoredPath))
        {
            return;
        }

        await _memory.DeleteDocumentAsync(collectionName, documentId, cancellationToken).ConfigureAwait(false);
        var tags = new TagCollection();
        await _memory.ImportDocumentAsync(entry.StoredPath, collectionName, tags, documentId, Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
        SaveEntry(entry with { UpdatedAtUtc = DateTimeOffset.UtcNow });
    }

    public string GetLastCollection() => LoadState().GetValueOrDefault("lastCollection")?.Trim() ?? "aimitra";

    public void SetLastCollection(string collection)
    {
        lock (_gate)
        {
            File.WriteAllText(_statePath, JsonSerializer.Serialize(new Dictionary<string, string> { ["lastCollection"] = GetCollectionName(collection) }, _jsonOptions));
        }
    }

    private string GetCollectionName(string? collection)
    {
        var name = string.IsNullOrWhiteSpace(collection) ? GetLastCollection() : collection.Trim();
        return string.IsNullOrWhiteSpace(name) ? "aimitra" : name;
    }

    private string GetCollectionFolder(string collectionName)
    {
        var folder = Path.Combine(_baseFolder, collectionName);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private string CopyIntoCollectionFolder(string sourcePath, string collectionName)
    {
        var storedFileName = $"{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}";
        var storedPath = Path.Combine(GetCollectionFolder(collectionName), storedFileName);
        File.Copy(sourcePath, storedPath, overwrite: true);
        return storedPath;
    }

    private void SaveEntry(DocumentMemoryEntry entry)
    {
        var entries = LoadEntries(entry.Collection);
        entries.RemoveAll(x => string.Equals(x.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
        entries.Add(entry);
        var path = GetEntriesPath(entry.Collection);
        File.WriteAllText(path, JsonSerializer.Serialize(entries, _jsonOptions));
    }

    private void RemoveEntry(string collectionName, string documentId)
    {
        var entries = LoadEntries(collectionName);
        entries.RemoveAll(x => string.Equals(x.Id, documentId, StringComparison.OrdinalIgnoreCase));
        File.WriteAllText(GetEntriesPath(collectionName), JsonSerializer.Serialize(entries, _jsonOptions));
    }

    private List<DocumentMemoryEntry> LoadEntries(string collectionName)
    {
        var path = GetEntriesPath(collectionName);
        if (!File.Exists(path)) return new List<DocumentMemoryEntry>();
        try
        {
            return JsonSerializer.Deserialize<List<DocumentMemoryEntry>>(File.ReadAllText(path), _jsonOptions) ?? new List<DocumentMemoryEntry>();
        }
        catch
        {
            return new List<DocumentMemoryEntry>();
        }
    }

    private string GetEntriesPath(string collectionName) => Path.Combine(GetCollectionFolder(collectionName), "entries.json");

    private Dictionary<string, string> LoadState()
    {
        if (!File.Exists(_statePath)) return new Dictionary<string, string>();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_statePath), _jsonOptions) ?? new Dictionary<string, string>(); }
        catch { return new Dictionary<string, string>(); }
    }

    private static string ExtractSearchText(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return string.Empty; }
    }

    private static string GetContentType(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".doc" or ".docx" => "application/msword",
            ".txt" => "text/plain",
            ".md" => "text/markdown",
            ".html" or ".htm" => "text/html",
            _ => "application/octet-stream"
        };
}
