namespace Aimitra.WebChat.Services;

#pragma warning disable SKEXP0050   // TextChunker is experimental in SK 1.x but stable enough for production use
using Microsoft.SemanticKernel.Text;

/// <summary>
/// Drop-in replacement for <see cref="DocumentMemoryService"/> that:
///   • Splits documents into paragraphs (chunks)
///   • Embeds each chunk with Google Gemini text-embedding-004
///   • Stores chunks + embeddings in SQLite via <see cref="SqliteVecDocumentStore"/>
///   • Answers questions by embedding the query and returning the most similar chunks
/// </summary>
public sealed class DocumentVectorStoreService : IDocumentMemoryService
{
    private readonly GeminiEmbeddingGenerator _embedder;
    private readonly SqliteVecDocumentStore   _store;

    public DocumentVectorStoreService(
        GeminiEmbeddingGenerator embedder,
        SqliteVecDocumentStore   store)
    {
        _embedder = embedder;
        _store    = store;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Import
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<string> ImportDocumentAsync(
        string filePath,
        string? collection = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Document not found.", filePath);

        var collectionName = ResolveCollection(collection);
        var text           = await TryReadTextAsync(filePath, cancellationToken);
        var entry          = BuildEntry(filePath, collectionName);

        _store.UpsertDocument(entry);
        var isMarkdown = entry.ContentType is "text/markdown";
        await EmbedAndStoreChunksAsync(entry.Id, collectionName, text, cancellationToken, isMarkdown);
        SetLastCollection(collectionName);
        return collectionName;
    }

    public async Task<string> ImportTextAsync(
        string title,
        string content,
        string? collection = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required.", nameof(content));

        var collectionName = ResolveCollection(collection);
        var id             = $"{Guid.NewGuid():N}";
        var entry = new DocumentMemoryEntry(
            Id:            id,
            Title:         string.IsNullOrWhiteSpace(title) ? id : title,
            FileName:      $"{id}.txt",
            Collection:    collectionName,
            StoredPath:    string.Empty,
            SearchTextPath: string.Empty,
            ContentType:   "text/plain",
            ImportedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc:  DateTimeOffset.UtcNow);

        _store.UpsertDocument(entry);
        await EmbedAndStoreChunksAsync(id, collectionName, content, cancellationToken);
        SetLastCollection(collectionName);
        return collectionName;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Semantic search
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<DocumentMemoryMatch>> AskAsync(
        string question,
        string? collection = null,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question is required.", nameof(question));

        var collectionName = ResolveCollection(collection);
        SetLastCollection(collectionName);

        var queryEmbedding = await _embedder.GenerateAsync(question, cancellationToken);
        var matches        = _store.SearchSimilar(queryEmbedding, collectionName, topK);

        return matches
            .Select(m => new DocumentMemoryMatch(
                Source:  m.FileName,
                Title:   m.Title,
                Snippet: m.Content.Length > 500 ? m.Content[..500] + "…" : m.Content,
                Score:   Math.Round(m.Score, 3)))
            .ToList();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Listing / deletion / reindex
    // ──────────────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<DocumentMemoryEntry>> ListDocumentsAsync(
        string? collection = null,
        CancellationToken cancellationToken = default)
    {
        var result = _store.ListDocuments(ResolveCollection(collection));
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(_store.ListCollections());
    }

    public Task DeleteDocumentAsync(
        string documentId,
        string? collection = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document id is required.", nameof(documentId));

        // Cascade delete removes chunks automatically (FK ON DELETE CASCADE)
        _store.DeleteDocument(documentId);
        return Task.CompletedTask;
    }

    public async Task ReindexDocumentAsync(
        string documentId,
        string? collection = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document id is required.", nameof(documentId));

        var entry = _store.GetDocument(documentId);
        if (entry is null || string.IsNullOrWhiteSpace(entry.StoredPath))
            return;

        var text = await TryReadTextAsync(entry.StoredPath, cancellationToken);
        _store.DeleteChunksForDocument(documentId);
        await EmbedAndStoreChunksAsync(documentId, entry.Collection, text, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Collection state
    // ──────────────────────────────────────────────────────────────────────────

    public string GetLastCollection() => _store.GetState("lastCollection", "aimitra");

    public void SetLastCollection(string collection) =>
        _store.SetState("lastCollection", ResolveCollection(collection));

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Splits <paramref name="text"/> into paragraphs, embeds each one with
    /// Gemini, and persists the chunk + embedding in the store.
    /// </summary>
    private async Task EmbedAndStoreChunksAsync(
        string documentId,
        string collection,
        string text,
        CancellationToken cancellationToken,
        bool isMarkdown = false)
    {
        var chunks = SplitIntoChunks(text, isMarkdown: isMarkdown);
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunkId   = $"{documentId}_chunk{i}";
            var embedding = await _embedder.GenerateAsync(chunks[i], cancellationToken);
            _store.UpsertChunk(chunkId, documentId, collection, chunks[i], embedding);
        }
    }

    /// <summary>
    /// Splits text into chunks using <see cref="TextChunker"/> from Semantic Kernel.
    ///
    /// Strategy
    /// ────────
    ///  1. <c>SplitPlainTextLines</c>      — break text into line-sized segments (≤ maxTokensPerChunk words).
    ///  2. <c>SplitPlainTextParagraphs</c> — group lines into final chunks with built-in word overlap.
    ///
    /// For Markdown documents (.md) use the overload with <paramref name="isMarkdown"/> = true,
    /// which respects headers, code blocks, and lists.
    ///
    /// Token unit: a "token" here is a whitespace-delimited word (not BPE).
    /// 200 words ≈ 1,200 chars — well within Gemini text-embedding-004's 2,048-token limit.
    /// </summary>
    private static IReadOnlyList<string> SplitIntoChunks(
        string text,
        int  maxTokensPerChunk = 200,
        int  overlapTokens     = 25,
        bool isMarkdown        = false)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var lines = isMarkdown
            ? TextChunker.SplitMarkDownLines(text, maxTokensPerLine: maxTokensPerChunk)
            : TextChunker.SplitPlainTextLines(text, maxTokensPerLine: maxTokensPerChunk);

        // SplitPlainTextParagraphs groups any line list into chunks with overlap
        var chunks = TextChunker.SplitPlainTextParagraphs(
            lines,
            maxTokensPerParagraph: maxTokensPerChunk,
            overlapTokens: overlapTokens);

        return chunks.Count == 0 ? new[] { text } : chunks;
    }

    private string ResolveCollection(string? collection)
    {
        var name = string.IsNullOrWhiteSpace(collection) ? GetLastCollection() : collection.Trim();
        return string.IsNullOrWhiteSpace(name) ? "aimitra" : name;
    }

    private static DocumentMemoryEntry BuildEntry(string filePath, string collection)
    {
        var guid = Guid.NewGuid().ToString("N");
        return new DocumentMemoryEntry(
            Id:            guid,
            Title:         Path.GetFileNameWithoutExtension(filePath),
            FileName:      Path.GetFileName(filePath),
            Collection:    collection,
            StoredPath:    filePath,
            SearchTextPath: string.Empty,
            ContentType:   GetContentType(filePath),
            ImportedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc:  DateTimeOffset.UtcNow);
    }

    private static async Task<string> TryReadTextAsync(string path, CancellationToken cancellationToken)
    {
        try   { return await File.ReadAllTextAsync(path, cancellationToken); }
        catch { return string.Empty; }
    }

    private static string GetContentType(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".pdf"            => "application/pdf",
            ".doc" or ".docx" => "application/msword",
            ".txt"            => "text/plain",
            ".md"             => "text/markdown",
            ".html" or ".htm" => "text/html",
            _                 => "application/octet-stream"
        };
}
