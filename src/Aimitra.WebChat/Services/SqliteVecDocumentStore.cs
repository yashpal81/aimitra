using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Aimitra.WebChat.Services;

/// <summary>
/// SQLite-backed document and vector store.
///
/// Schema
/// ──────
///   documents      – document-level metadata (id, title, file_name, …)
///   chunks         – text paragraphs split from each document
///   chunk_vectors  – float32 embeddings, one row per chunk
///
/// Vector search
/// ─────────────
/// By default, cosine similarity is computed in C# after loading candidate
/// embeddings from SQLite.  This is fast enough for tens of thousands of chunks.
///
/// To upgrade to the native sqlite-vec extension for ANN search at scale:
///   1. Add the sqlite-vec native binary to the output folder
///      (vec0.dll on Windows, libvec0.so on Linux, libvec0.dylib on macOS).
///      Download from https://github.com/asg017/sqlite-vec/releases
///   2. Uncomment the three lines marked "// sqlite-vec" in this file.
///   3. Replace the C# cosine-similarity loop with the commented-out SQL query.
/// </summary>
public sealed class SqliteVecDocumentStore : IDisposable
{
    private readonly string _connectionString;
    private readonly int    _dimensions;
    private bool            _disposed;

    public SqliteVecDocumentStore(string dbPath, int dimensions = 768)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode       = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        _dimensions = dimensions;
        InitSchema();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Schema
    // ──────────────────────────────────────────────────────────────────────────

    private void InitSchema()
    {
        using var conn = Open();
        Execute(conn, string.Format(@"
            CREATE TABLE IF NOT EXISTS documents (
                id           TEXT PRIMARY KEY,
                title        TEXT NOT NULL,
                file_name    TEXT NOT NULL,
                collection   TEXT NOT NULL,
                stored_path  TEXT NOT NULL,
                content_type TEXT NOT NULL,
                imported_at  TEXT NOT NULL,
                updated_at   TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS chunks (
                id          TEXT PRIMARY KEY,
                document_id TEXT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
                collection  TEXT NOT NULL,
                content     TEXT NOT NULL,
                embedding   TEXT NOT NULL   -- JSON float32 array
            );

            CREATE INDEX IF NOT EXISTS idx_chunks_collection ON chunks(collection);
            CREATE INDEX IF NOT EXISTS idx_chunks_document   ON chunks(document_id);

            CREATE TABLE IF NOT EXISTS km_state (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            "));

        /* ── sqlite-vec alternative ──────────────────────────────────────────
        // Uncomment when vec0 extension is available:
        conn.EnableExtensions(true);
        conn.LoadExtension("vec0");   // ensure vec0 binary is in the output folder
        Execute(conn, string.Format(@"
            CREATE VIRTUAL TABLE IF NOT EXISTS chunk_vectors USING vec0(
                chunk_id  TEXT PRIMARY KEY,
                embedding FLOAT[{0}]
            );
        ", _dimensions));
        ─────────────────────────────────────────────────────────────────────── */
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Document CRUD
    // ──────────────────────────────────────────────────────────────────────────

    public void UpsertDocument(DocumentMemoryEntry entry)
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO documents (id, title, file_name, collection, stored_path, content_type, imported_at, updated_at)
            VALUES (@id, @title, @fileName, @collection, @storedPath, @contentType, @importedAt, @updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                title       = excluded.title,
                file_name   = excluded.file_name,
                updated_at  = excluded.updated_at;
            ";
        cmd.Parameters.AddWithValue("@id",          entry.Id);
        cmd.Parameters.AddWithValue("@title",        entry.Title);
        cmd.Parameters.AddWithValue("@fileName",     entry.FileName);
        cmd.Parameters.AddWithValue("@collection",   entry.Collection);
        cmd.Parameters.AddWithValue("@storedPath",   entry.StoredPath);
        cmd.Parameters.AddWithValue("@contentType",  entry.ContentType);
        cmd.Parameters.AddWithValue("@importedAt",   entry.ImportedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@updatedAt",    entry.UpdatedAtUtc.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void DeleteDocument(string documentId)
    {
        using var conn = Open();
        // cascade deletes chunks
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM documents WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", documentId);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<DocumentMemoryEntry> ListDocuments(string collection)
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, title, file_name, collection, stored_path, content_type, imported_at, updated_at
            FROM   documents
            WHERE  collection = @collection
            ORDER  BY updated_at DESC;
            ";
        cmd.Parameters.AddWithValue("@collection", collection);
        var result = new List<DocumentMemoryEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(ReadEntry(reader));
        return result;
    }

    public IReadOnlyList<string> ListCollections()
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT collection
            FROM   documents
            ORDER  BY collection COLLATE NOCASE;
            ";

        var result = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var collection = reader.GetString(0);
            if (!string.IsNullOrWhiteSpace(collection))
            {
                result.Add(collection);
            }
        }

        return result;
    }

    public DocumentMemoryEntry? GetDocument(string documentId)
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, title, file_name, collection, stored_path, content_type, imported_at, updated_at
            FROM   documents WHERE id = @id;
            ";
        cmd.Parameters.AddWithValue("@id", documentId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadEntry(reader) : null;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Chunk + embedding upsert
    // ──────────────────────────────────────────────────────────────────────────

    public void UpsertChunk(string chunkId, string documentId, string collection, string content, float[] embedding)
    {
        var embeddingJson = JsonSerializer.Serialize(embedding);
        using var conn    = Open();
        using var cmd     = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO chunks (id, document_id, collection, content, embedding)
            VALUES (@id, @docId, @collection, @content, @embedding)
            ON CONFLICT(id) DO UPDATE SET
                content   = excluded.content,
                embedding = excluded.embedding;
            ";
        cmd.Parameters.AddWithValue("@id",         chunkId);
        cmd.Parameters.AddWithValue("@docId",       documentId);
        cmd.Parameters.AddWithValue("@collection",  collection);
        cmd.Parameters.AddWithValue("@content",     content);
        cmd.Parameters.AddWithValue("@embedding",   embeddingJson);
        cmd.ExecuteNonQuery();

        /* ── sqlite-vec alternative ──────────────────────────────────────────
        using var vecCmd = conn.CreateCommand();
        vecCmd.CommandText = @"
            INSERT INTO chunk_vectors (chunk_id, embedding)
            VALUES (@chunkId, @embedding)
            ON CONFLICT(chunk_id) DO UPDATE SET embedding = excluded.embedding;
        ";
        vecCmd.Parameters.AddWithValue("@chunkId", chunkId);
        vecCmd.Parameters.Add(new SqliteParameter("@embedding", SqliteType.Blob)
            { Value = FloatsToBlob(embedding) });
        vecCmd.ExecuteNonQuery();
        ─────────────────────────────────────────────────────────────────────── */
    }

    public void DeleteChunksForDocument(string documentId)
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM chunks WHERE document_id = @docId;";
        cmd.Parameters.AddWithValue("@docId", documentId);
        cmd.ExecuteNonQuery();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Vector similarity search
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the top-<paramref name="topK"/> chunks ranked by cosine similarity
    /// to <paramref name="queryEmbedding"/>.
    /// </summary>
    public IReadOnlyList<ChunkMatch> SearchSimilar(float[] queryEmbedding, string collection, int topK)
    {
        // ── Option A (current): load embeddings and rank in C# ───────────────
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT c.id, c.document_id, c.content, c.embedding,
                   d.title, d.file_name
            FROM   chunks    c
            JOIN   documents d ON d.id = c.document_id
            WHERE  c.collection = @collection;
            ";
        cmd.Parameters.AddWithValue("@collection", collection);

        var candidates = new List<(string chunkId, string docId, string content, float[] vec, string title, string fileName)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var vec = JsonSerializer.Deserialize<float[]>(reader.GetString(3)) ?? Array.Empty<float>();
                candidates.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2),
                                vec, reader.GetString(4), reader.GetString(5)));
            }
        }

        return candidates
            .Select(c => new ChunkMatch(
                ChunkId:    c.chunkId,
                DocumentId: c.docId,
                Content:    c.content,
                Title:      c.title,
                FileName:   c.fileName,
                Score:      CosineSimilarity(queryEmbedding, c.vec)))
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();

        /* ── Option B: sqlite-vec ANN search (uncomment when vec0 is loaded) ──
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT cv.chunk_id, c.document_id, c.content, cv.distance,
                   d.title, d.file_name
            FROM   chunk_vectors cv
            JOIN   chunks        c ON c.id = cv.chunk_id
            JOIN   documents     d ON d.id = c.document_id
            WHERE  cv.embedding MATCH @query
              AND  cv.k         = @k
            ORDER  BY cv.distance;
        """;
        cmd.Parameters.Add(new SqliteParameter("@query", SqliteType.Blob)
            { Value = FloatsToBlob(queryEmbedding) });
        cmd.Parameters.AddWithValue("@k", topK * 3);
        // post-filter by collection then take topK
        ─────────────────────────────────────────────────────────────────────── */
    }

    // ──────────────────────────────────────────────────────────────────────────
    // State (last collection)
    // ──────────────────────────────────────────────────────────────────────────

    public string GetState(string key, string defaultValue = "aimitra")
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM km_state WHERE key = @key;";
        cmd.Parameters.AddWithValue("@key", key);
        var val = cmd.ExecuteScalar() as string;
        return string.IsNullOrWhiteSpace(val) ? defaultValue : val;
    }

    public void SetState(string key, string value)
    {
        using var conn = Open();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO km_state (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            ";
        cmd.Parameters.AddWithValue("@key",   key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static DocumentMemoryEntry ReadEntry(SqliteDataReader r) =>
        new(Id:           r.GetString(0),
            Title:        r.GetString(1),
            FileName:     r.GetString(2),
            Collection:   r.GetString(3),
            StoredPath:   r.GetString(4),
            SearchTextPath: string.Empty,
            ContentType:  r.GetString(5),
            ImportedAtUtc: DateTimeOffset.Parse(r.GetString(6)),
            UpdatedAtUtc:  DateTimeOffset.Parse(r.GetString(7)));

    /// <summary>Cosine similarity between two equal-length vectors.</summary>
    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot   += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom < double.Epsilon ? 0 : dot / denom;
    }

    /// <summary>
    /// Serialises a float array to a raw IEEE-754 little-endian BLOB
    /// compatible with the sqlite-vec FLOAT[n] column type.
    /// </summary>
    private static byte[] FloatsToBlob(float[] values)
    {
        var blob = new byte[values.Length * sizeof(float)];
        MemoryMarshal.AsBytes(values.AsSpan()).CopyTo(blob);
        return blob;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}

public sealed record ChunkMatch(
    string ChunkId,
    string DocumentId,
    string Content,
    string Title,
    string FileName,
    double Score);
