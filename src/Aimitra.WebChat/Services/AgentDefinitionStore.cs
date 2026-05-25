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

        public string Save(string filePath, AgentDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return Save(definition);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? GetDefinitionsFolder());
            var json = JsonSerializer.Serialize(definition, _jsonOptions);
            File.WriteAllText(filePath, json);
            return filePath;
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
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            return LoadFile(filePath);
        }

        private string GetDefinitionsFolder()
        {
            return Path.Combine(AppContext.BaseDirectory, "App_Data", "agent-definitions");
        }

        private string GetPromptPluginsFolder()
        {
            return Path.Combine(AppContext.BaseDirectory, "App_Data", "Plugins");
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
