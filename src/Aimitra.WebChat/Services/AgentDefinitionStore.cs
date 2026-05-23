using System.Text.Json;
using Aimitra.WebChat.Models;

namespace Aimitra.WebChat.Services
{
    public class AgentDefinitionStore
    {
        private readonly IWebHostEnvironment _environment;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };

        public AgentDefinitionStore(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        public IReadOnlyList<AgentDefinitionFile> LoadAll()
        {
            var folder = GetDefinitionsFolder();
            if (!Directory.Exists(folder))
            {
                return Array.Empty<AgentDefinitionFile>();
            }

            return Directory.EnumerateFiles(folder, "*.json")
                .Select(filePath => LoadFile(filePath))
                .Where(file => file is not null)
                .Select(file => file!)
                .OrderByDescending(file => file.Definition.Active)
                .ThenBy(file => file.Definition.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public void SaveAll(IEnumerable<AgentDefinitionFile> files)
        {
            foreach (var file in files)
            {
                var json = JsonSerializer.Serialize(file.Definition, _jsonOptions);
                File.WriteAllText(file.FilePath, json);
            }
        }

        public string Save(AgentDefinition definition)
        {
            var folder = GetDefinitionsFolder();
            Directory.CreateDirectory(folder);

            var fileName = MakeSafeFileName(definition.Name);
            var filePath = Path.Combine(folder, $"{fileName}.json");
            var json = JsonSerializer.Serialize(definition, _jsonOptions);
            File.WriteAllText(filePath, json);

            return filePath;
        }

        private string GetDefinitionsFolder()
        {
            return Path.Combine(AppContext.BaseDirectory, "App_Data", "agent-definitions");
        }

        private static AgentDefinitionFile? LoadFile(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var definition = JsonSerializer.Deserialize<AgentDefinition>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

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

        private static string MakeSafeFileName(string name)
        {
            var safeName = string.Join("_", name.Trim().Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(safeName) ? "agent-definition" : safeName;
        }
    }

    public sealed record AgentDefinitionFile(string FilePath, AgentDefinition Definition);
}
