using System.Text.Json;

namespace Aimitra.WebChat.Services
{
    public sealed record DocumentMemoryMatch(
        string Source,
        string Title,
        string Snippet,
        double? Score = null);

    public sealed record DocumentMemoryEntry(
        string Id,
        string Title,
        string FileName,
        string Collection,
        string StoredPath,
        string ContentType,
        DateTimeOffset ImportedAtUtc,
        DateTimeOffset UpdatedAtUtc);

    public interface IDocumentMemoryService
    {
        Task<string> ImportDocumentAsync(string filePath, string? collection = null, CancellationToken cancellationToken = default);
        Task<string> ImportTextAsync(string title, string content, string? collection = null, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<DocumentMemoryMatch>> AskAsync(string question, string? collection = null, int topK = 5, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<DocumentMemoryEntry>> ListDocumentsAsync(string? collection = null, CancellationToken cancellationToken = default);
        Task DeleteDocumentAsync(string documentId, string? collection = null, CancellationToken cancellationToken = default);
        Task ReindexDocumentAsync(string documentId, string? collection = null, CancellationToken cancellationToken = default);
        string GetLastCollection();
        void SetLastCollection(string collection);
    }

    public sealed class DocumentMemoryService : IDocumentMemoryService
    {
        private readonly string _baseFolder;
        private readonly string _statePath;
        private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly object _gate = new();

        public DocumentMemoryService()
        {
            _baseFolder = Path.Combine(AppContext.BaseDirectory, "App_Data", "knowledge-base");
            _statePath = Path.Combine(_baseFolder, "state.json");
            Directory.CreateDirectory(_baseFolder);
        }

        public Task<string> ImportDocumentAsync(string filePath, string? collection = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path is required.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Document not found.", filePath);
            }

            var collectionName = GetCollectionName(collection);
            var documents = LoadEntries(collectionName);
            var storedFileName = $"{Guid.NewGuid():N}{Path.GetExtension(filePath)}";
            var storedPath = Path.Combine(GetCollectionFolder(collectionName), storedFileName);
            File.Copy(filePath, storedPath, overwrite: true);

            var entry = new DocumentMemoryEntry(
                Id: Path.GetFileNameWithoutExtension(storedFileName),
                Title: Path.GetFileNameWithoutExtension(filePath),
                FileName: Path.GetFileName(filePath),
                Collection: collectionName,
                StoredPath: storedPath,
                ContentType: GetContentType(filePath),
                ImportedAtUtc: DateTimeOffset.UtcNow,
                UpdatedAtUtc: DateTimeOffset.UtcNow);

            documents.RemoveAll(d => d.Title.Equals(entry.Title, StringComparison.OrdinalIgnoreCase));
            documents.Add(entry);
            SaveEntries(collectionName, documents);
            SetLastCollection(collectionName);
            return Task.FromResult(collectionName);
        }

        public Task<string> ImportTextAsync(string title, string content, string? collection = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new ArgumentException("Content is required.", nameof(content));
            }

            var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
            return ImportTextInternalAsync(title, content, collection, tempFile, cancellationToken);
        }

        public Task<IReadOnlyList<DocumentMemoryMatch>> AskAsync(string question, string? collection = null, int topK = 5, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(question))
            {
                throw new ArgumentException("Question is required.", nameof(question));
            }

            var collectionName = GetCollectionName(collection);
            SetLastCollection(collectionName);
            var entries = LoadEntries(collectionName);

            var questionTokens = Tokenize(question);
            var scored = new List<(DocumentMemoryEntry Entry, double Score, string Snippet)>();

            foreach (var entry in entries)
            {
                var text = TryReadText(entry.StoredPath);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var bestSnippet = BuildBestSnippet(text, questionTokens, out var score);
                if (score <= 0)
                {
                    continue;
                }

                scored.Add((entry, score, bestSnippet));
            }

            var matches = scored
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Entry.UpdatedAtUtc)
                .Take(topK)
                .Select(x => new DocumentMemoryMatch(
                    Source: x.Entry.FileName,
                    Title: x.Entry.Title,
                    Snippet: x.Snippet,
                    Score: Math.Round(x.Score, 3)))
                .ToList();

            return Task.FromResult<IReadOnlyList<DocumentMemoryMatch>>(matches);
        }

        public Task<IReadOnlyList<DocumentMemoryEntry>> ListDocumentsAsync(string? collection = null, CancellationToken cancellationToken = default)
        {
            var collectionName = GetCollectionName(collection);
            var entries = LoadEntries(collectionName);
            return Task.FromResult<IReadOnlyList<DocumentMemoryEntry>>(entries
                .OrderByDescending(x => x.UpdatedAtUtc)
                .ToList());
        }

        public Task DeleteDocumentAsync(string documentId, string? collection = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(documentId))
            {
                throw new ArgumentException("Document id is required.", nameof(documentId));
            }

            var collectionName = GetCollectionName(collection);
            var entries = LoadEntries(collectionName);
            var removed = entries.FirstOrDefault(x => string.Equals(x.Id, documentId, StringComparison.OrdinalIgnoreCase));
            if (removed is null)
            {
                return Task.CompletedTask;
            }

            if (File.Exists(removed.StoredPath))
            {
                File.Delete(removed.StoredPath);
            }

            entries.RemoveAll(x => string.Equals(x.Id, documentId, StringComparison.OrdinalIgnoreCase));
            SaveEntries(collectionName, entries);
            return Task.CompletedTask;
        }

        public Task ReindexDocumentAsync(string documentId, string? collection = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(documentId))
            {
                throw new ArgumentException("Document id is required.", nameof(documentId));
            }

            var collectionName = GetCollectionName(collection);
            var entries = LoadEntries(collectionName);
            var entry = entries.FirstOrDefault(x => string.Equals(x.Id, documentId, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return Task.CompletedTask;
            }

            var updated = entry with { UpdatedAtUtc = DateTimeOffset.UtcNow };
            entries.RemoveAll(x => string.Equals(x.Id, documentId, StringComparison.OrdinalIgnoreCase));
            entries.Add(updated);
            SaveEntries(collectionName, entries);
            return Task.CompletedTask;
        }

        public string GetLastCollection()
        {
            lock (_gate)
            {
                if (!File.Exists(_statePath))
                {
                    return "aimitra";
                }

                try
                {
                    var json = File.ReadAllText(_statePath);
                    var state = JsonSerializer.Deserialize<Dictionary<string, string>>(json, _jsonOptions);
                    return state?.GetValueOrDefault("lastCollection")?.Trim() ?? "aimitra";
                }
                catch
                {
                    return "aimitra";
                }
            }
        }

        public void SetLastCollection(string collection)
        {
            var state = new Dictionary<string, string>
            {
                ["lastCollection"] = GetCollectionName(collection)
            };

            lock (_gate)
            {
                Directory.CreateDirectory(_baseFolder);
                File.WriteAllText(_statePath, JsonSerializer.Serialize(state, _jsonOptions));
            }
        }

        private Task<string> ImportTextInternalAsync(string title, string content, string? collection, string tempFile, CancellationToken cancellationToken)
        {
            return ImportTextCoreAsync(title, content, collection, tempFile, cancellationToken);
        }

        private async Task<string> ImportTextCoreAsync(string title, string content, string? collection, string tempFile, CancellationToken cancellationToken)
        {
            try
            {
                await File.WriteAllTextAsync(tempFile, content, cancellationToken).ConfigureAwait(false);
                return await ImportDocumentAsync(tempFile, collection, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        private string GetCollectionName(string? collection)
        {
            var name = string.IsNullOrWhiteSpace(collection) ? GetLastCollection() : collection.Trim();
            return string.IsNullOrWhiteSpace(name) ? "aimitra" : name;
        }

        private string GetCollectionFolder(string collection)
        {
            var folder = Path.Combine(_baseFolder, collection);
            Directory.CreateDirectory(folder);
            return folder;
        }

        private List<DocumentMemoryEntry> LoadEntries(string collection)
        {
            var path = GetCollectionCatalogPath(collection);
            if (!File.Exists(path))
            {
                return new List<DocumentMemoryEntry>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<DocumentMemoryEntry>>(File.ReadAllText(path), _jsonOptions) ?? new List<DocumentMemoryEntry>();
            }
            catch
            {
                return new List<DocumentMemoryEntry>();
            }
        }

        private void SaveEntries(string collection, List<DocumentMemoryEntry> entries)
        {
            var path = GetCollectionCatalogPath(collection);
            File.WriteAllText(path, JsonSerializer.Serialize(entries, _jsonOptions));
        }

        private string GetCollectionCatalogPath(string collection)
        {
            return Path.Combine(GetCollectionFolder(collection), "documents.json");
        }

        private static string GetContentType(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "application/pdf",
                ".doc" or ".docx" => "application/msword",
                ".txt" => "text/plain",
                ".md" => "text/markdown",
                ".html" or ".htm" => "text/html",
                _ => "application/octet-stream"
            };
        }

        private static string TryReadText(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static HashSet<string> Tokenize(string text)
        {
            return text
                .Split(new[] { ' ', '\r', '\n', '\t', ',', '.', ';', ':', '/', '\\', '-', '_', '(', ')', '[', ']', '{', '}', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLowerInvariant())
                .Where(t => t.Length > 2)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static string BuildBestSnippet(string content, HashSet<string> questionTokens, out double score)
        {
            var paragraphs = content
                .Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToList();

            if (paragraphs.Count == 0)
            {
                score = 0;
                return string.Empty;
            }

            var best = paragraphs
                .Select(p => new
                {
                    Text = p,
                    Score = ScoreText(p, questionTokens)
                })
                .OrderByDescending(x => x.Score)
                .First();

            score = best.Score;
            return best.Text.Length > 500 ? best.Text[..500] + "..." : best.Text;
        }

        private static double ScoreText(string text, HashSet<string> questionTokens)
        {
            var words = Tokenize(text);
            if (words.Count == 0 || questionTokens.Count == 0)
            {
                return 0;
            }

            var overlap = words.Intersect(questionTokens, StringComparer.OrdinalIgnoreCase).Count();
            return (double)overlap / Math.Max(1, questionTokens.Count);
        }
    }
}
