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

        public string Save(AgentDefinition definition)
        {
            var folder = Path.Combine(_environment.ContentRootPath, "App_Data", "agent-definitions");
            Directory.CreateDirectory(folder);

            var fileName = MakeSafeFileName(definition.Name);
            var filePath = Path.Combine(folder, $"{fileName}.json");
            var json = JsonSerializer.Serialize(definition, _jsonOptions);
            File.WriteAllText(filePath, json);

            return filePath;
        }

        private static string MakeSafeFileName(string name)
        {
            var safeName = string.Join("_", name.Trim().Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(safeName) ? "agent-definition" : safeName;
        }
    }
}
