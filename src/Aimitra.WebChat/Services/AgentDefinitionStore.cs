using System.Text.Json;
using METASYNAPSE.WebChat.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;

namespace METASYNAPSE.WebChat.Services
{
    public class AgentDefinitionStore
    {
        private readonly IWebHostEnvironment _environment;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };
        private readonly string _definitionsFolder;
        private readonly string _databasePath;
        private readonly object _syncRoot = new();

        public AgentDefinitionStore(IWebHostEnvironment environment)
        {
            _environment = environment;
            _definitionsFolder = Path.Combine(_environment.ContentRootPath, "App_Data", "agent-definitions");
            _databasePath = Path.Combine(_definitionsFolder, "definitions.sqlite");
            Directory.CreateDirectory(_definitionsFolder);
            InitializeDatabase();
            MigrateLegacyJsonFiles();
        }

        public IReadOnlyList<AgentDefinitionFile> LoadAll()
        {
            lock (_syncRoot)
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT file_path, json_payload, active
                    FROM agent_definitions
                    ORDER BY active DESC, name ASC";

                using var reader = command.ExecuteReader();
                var results = new List<AgentDefinitionFile>();
                while (reader.Read())
                {
                    var definition = Deserialize(reader.GetString(1));
                    if (definition is null)
                    {
                        continue;
                    }

                    definition.Active = reader.GetInt32(2) == 1;
                    results.Add(new AgentDefinitionFile(reader.GetString(0), definition));
                }

                return results;
            }
        }

        public void SaveAll(IEnumerable<AgentDefinitionFile> files)
        {
            foreach (var file in files)
            {
                SaveInternal(file.FilePath, file.Definition, true);
            }
        }

        public string Save(AgentDefinition definition)
        {
            var fileName = MakeSafeFileName(definition.Name);
            var filePath = Path.Combine(_definitionsFolder, $"{fileName}.json");
            SaveInternal(filePath, definition, false);
            return filePath;
        }

        public string Save(string filePath, AgentDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return Save(definition);
            }

            SaveInternal(ResolveFilePath(filePath), definition, false);
            return ResolveFilePath(filePath);
        }

        public string SavePromptPlugin(string pluginName, string functionName, string promptTemplate, string description)
        {
            var safePluginName = MakeSafeFileName(pluginName);
            var safeFunctionName = MakeSafeFileName(functionName);
            var functionFolder = Path.Combine(GetPromptPluginsFolder(), safePluginName, safeFunctionName);
            Directory.CreateDirectory(functionFolder);

            File.WriteAllText(Path.Combine(functionFolder, "skprompt.txt"), promptTemplate ?? string.Empty);

            var escapedDescription = (description ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
            var configJson =
                "{\n" +
                "  \"schema\": 1,\n" +
                $"  \"description\": \"{escapedDescription}\",\n" +
                "  \"execution_settings\": {\n" +
                "    \"default\": {\n" +
                "      \"max_tokens\": 500,\n" +
                "      \"temperature\": 0.3\n" +
                "    }\n" +
                "  },\n" +
                "  \"input_variables\": [\n" +
                "    {\n" +
                "      \"name\": \"input\",\n" +
                "      \"description\": \"The raw text or request from the user.\",\n" +
                "      \"required\": true\n" +
                "    },\n" +
                "    {\n" +
                "      \"name\": \"currentDateTime\",\n" +
                "      \"description\": \"The current date and time to anchor relative dates like 'tomorrow' or 'next Monday'.\",\n" +
                "      \"required\": false\n" +
                "    }\n" +
                "  ]\n" +
                "}";

            File.WriteAllText(Path.Combine(functionFolder, "config.json"), configJson);

            return functionFolder;
        }

        public AgentDefinitionFile? Load(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            var resolvedPath = ResolveFilePath(filePath);
            lock (_syncRoot)
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT file_path, json_payload, active
                    FROM agent_definitions
                    WHERE file_path = @filePath
                    LIMIT 1";
                command.Parameters.AddWithValue("@filePath", resolvedPath);

                using var reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    return null;
                }

                var definition = Deserialize(reader.GetString(1));
                if (definition is null)
                {
                    return null;
                }

                definition.Active = reader.GetInt32(2) == 1;
                return new AgentDefinitionFile(reader.GetString(0), definition);
            }
        }

        private void InitializeDatabase()
        {
            lock (_syncRoot)
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS agent_definitions (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        file_path TEXT NOT NULL UNIQUE,
                        name TEXT NOT NULL,
                        active INTEGER NOT NULL DEFAULT 0,
                        json_payload TEXT NOT NULL,
                        updated_at TEXT NOT NULL
                    )";
                command.ExecuteNonQuery();
            }
        }

        private void MigrateLegacyJsonFiles()
        {
            if (!Directory.Exists(_definitionsFolder))
            {
                return;
            }

            var legacyPaths = Directory.EnumerateFiles(_definitionsFolder, "*.json", SearchOption.TopDirectoryOnly)
                .Where(path => !string.Equals(Path.GetFileName(path), "definitions.sqlite", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var legacyPath in legacyPaths)
            {
                var resolvedPath = ResolveFilePath(legacyPath);
                if (RecordExists(resolvedPath))
                {
                    continue;
                }

                var legacyDefinition = LoadLegacyDefinition(legacyPath);
                if (legacyDefinition is not null)
                {
                    SaveInternal(resolvedPath, legacyDefinition.Definition, false);
                }
            }
        }

        private bool RecordExists(string filePath)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM agent_definitions WHERE file_path = @filePath LIMIT 1";
            command.Parameters.AddWithValue("@filePath", filePath);
            using var reader = command.ExecuteReader();
            return reader.Read();
        }

        private void SaveInternal(string filePath, AgentDefinition definition, bool updateActive)
        {
            var resolvedPath = ResolveFilePath(filePath);
            var json = JsonSerializer.Serialize(definition, _jsonOptions);
            var active = definition.Active ? 1 : 0;
            var updatedAt = DateTimeOffset.UtcNow.ToString("O");

            lock (_syncRoot)
            {
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO agent_definitions (file_path, name, active, json_payload, updated_at)
                    VALUES (@filePath, @name, @active, @jsonPayload, @updatedAt)
                    ON CONFLICT(file_path) DO UPDATE SET
                        name = excluded.name,
                        active = excluded.active,
                        json_payload = excluded.json_payload,
                        updated_at = excluded.updated_at";
                command.Parameters.AddWithValue("@filePath", resolvedPath);
                command.Parameters.AddWithValue("@name", definition.Name);
                command.Parameters.AddWithValue("@active", active);
                command.Parameters.AddWithValue("@jsonPayload", json);
                command.Parameters.AddWithValue("@updatedAt", updatedAt);
                command.ExecuteNonQuery();
            }

            if (updateActive)
            {
                UpdateActiveFlag(resolvedPath, definition.Active);
            }
        }

        private void UpdateActiveFlag(string filePath, bool active)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE agent_definitions SET active = @active WHERE file_path = @filePath";
            command.Parameters.AddWithValue("@active", active ? 1 : 0);
            command.Parameters.AddWithValue("@filePath", filePath);
            command.ExecuteNonQuery();
        }

        private AgentDefinitionFile? LoadLegacyDefinition(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var definition = Deserialize(json);
                if (definition is null)
                {
                    return null;
                }

                return new AgentDefinitionFile(filePath, definition);
            }
            catch
            {
                return null;
            }
        }

        private AgentDefinition? Deserialize(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<AgentDefinition>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return null;
            }
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection($"Data Source={_databasePath}");
            connection.Open();
            return connection;
        }

        private string ResolveFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return Path.Combine(_definitionsFolder, "agent-definition.json");
            }

            return Path.IsPathRooted(filePath)
                ? Path.GetFullPath(filePath)
                : Path.GetFullPath(Path.Combine(_definitionsFolder, filePath));
        }

        private string GetPromptPluginsFolder()
        {
            return Path.Combine(_environment.ContentRootPath, "App_Data", "Plugins");
        }

        private static string MakeSafeFileName(string name)
        {
            var safeName = string.Join("_", name.Trim().Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(safeName) ? "agent-definition" : safeName;
        }
    }

    public sealed record AgentDefinitionFile(string FilePath, AgentDefinition Definition);
}

