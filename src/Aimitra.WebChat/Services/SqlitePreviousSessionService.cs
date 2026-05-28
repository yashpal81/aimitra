using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Aimitra.WebChat.Services
{
    public class SqlitePreviousSessionService : IPreviousSessionService, IDisposable
    {
        private readonly string _dbPath;
        private readonly SqliteConnection _connection;

        public SqlitePreviousSessionService(string dbPath)
        {
            _dbPath = dbPath;
            var cs = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();
            _connection = new SqliteConnection(cs);
            _connection.Open();
            EnsureTable();
            EnsureMessagesTable();
        }

        private void EnsureTable()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS previous_sessions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL UNIQUE,
                    last_seen TEXT NOT NULL
                );";
            cmd.ExecuteNonQuery();
        }

        // messages table stores ordered messages per session
        private void EnsureMessagesTable()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS previous_session_messages (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    session_name TEXT NOT NULL,
                    message_id TEXT,
                    user TEXT NOT NULL,
                    text TEXT NOT NULL,
                    is_partial INTEGER NOT NULL DEFAULT 0,
                    ts TEXT NOT NULL
                );";
            cmd.ExecuteNonQuery();
        }

        public async Task<List<string>> GetRecentSessionsAsync(int limit = 20)
        {
            var results = new List<string>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"SELECT name FROM previous_sessions ORDER BY datetime(last_seen) DESC LIMIT $limit";
            cmd.Parameters.AddWithValue("$limit", limit);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                results.Add(rdr.GetString(0));
            }
            return results;
        }

        public async Task<string?> GetLastSessionNameAsync()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"SELECT name FROM previous_sessions ORDER BY datetime(last_seen) DESC LIMIT 1";
            var res = await cmd.ExecuteScalarAsync();
            return res is null ? null : Convert.ToString(res);
        }

        public async Task<List<PreviousSessionMessage>> GetSessionMessagesAsync(string sessionName)
        {
            var results = new List<PreviousSessionMessage>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"SELECT message_id, user, text, is_partial, ts FROM previous_session_messages WHERE session_name = $name ORDER BY id ASC";
            cmd.Parameters.AddWithValue("$name", sessionName);
            using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var msg = new PreviousSessionMessage
                {
                    MessageId = rdr.IsDBNull(0) ? string.Empty : rdr.GetString(0),
                    User = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1),
                    Text = rdr.IsDBNull(2) ? string.Empty : rdr.GetString(2),
                    IsPartial = !rdr.IsDBNull(3) && rdr.GetInt32(3) != 0,
                    Timestamp = rdr.IsDBNull(4) ? string.Empty : rdr.GetString(4)
                };
                results.Add(msg);
            }
            return results;
        }

        public async Task AddMessageAsync(string sessionName, string? messageId, string user, string message, DateTime timestamp, bool isPartial = false)
        {
            using var tx = _connection.BeginTransaction();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO previous_session_messages(session_name, message_id, user, text, is_partial, ts)
                                 VALUES($session, $mid, $user, $text, $isPartial, $ts);";
            cmd.Parameters.AddWithValue("$session", sessionName);
            cmd.Parameters.AddWithValue("$mid", (object?)messageId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$user", user);
            cmd.Parameters.AddWithValue("$text", message);
            cmd.Parameters.AddWithValue("$isPartial", isPartial ? 1 : 0);
            cmd.Parameters.AddWithValue("$ts", timestamp.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
            tx.Commit();
        }

        public async Task AddOrUpdateSessionAsync(string name)
        {
            using var tx = _connection.BeginTransaction();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO previous_sessions(name, last_seen) VALUES($name, $now)
                                  ON CONFLICT(name) DO UPDATE SET last_seen = $now;";
            cmd.Parameters.AddWithValue("$name", name);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync();
            tx.Commit();
        }

        public void Dispose()
        {
            try
            {
                _connection?.Close();
                _connection?.Dispose();
            }
            catch { }
        }
    }
}
