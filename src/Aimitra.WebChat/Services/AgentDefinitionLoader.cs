using System.Text.Json;
using Aimitra.Core.Models;
using Aimitra.SamplePlugins.Plugins;
using Aimitra.Security.Guardrails;
using Aimitra.Services.Orchestration;
using Aimitra.Services.Plugins;
using Aimitra.WebChat.Models;
using Microsoft.SemanticKernel;

namespace Aimitra.WebChat.Services
{
    public sealed class AgentDefinitionLoader
    {
        private readonly IWebHostEnvironment _environment;

        public AgentDefinitionLoader(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        public Topic[] LoadActiveTopics()
        {
            
            Console.WriteLine($"Starting to load agent definitions from disk at {DateTime.UtcNow}...");  
            var folder = Path.Combine(_environment.ContentRootPath, "App_Data", "agent-definitions");
            if (!Directory.Exists(folder))
            {
                return Array.Empty<Topic>();
            }

            var definitions = Directory.EnumerateFiles(folder, "*.json")
                .Select(LoadDefinition)
                .Where(definition => definition is not null && definition.Active)
                .Select(definition => definition!)
                .ToList();

            if (definitions.Count == 0)
            {
                return Array.Empty<Topic>();
            }

            var pluginCatalog = BuildPluginCatalog();
            var topics = new List<Topic>();

            foreach (var definition in definitions)
            {
                foreach (var topicDefinition in definition.Topics)
                {
                    var plugins = ResolvePlugins(topicDefinition.KernelFunctions, pluginCatalog);
                    topics.Add(new Topic(
                        Name: topicDefinition.Name,
                        Description: topicDefinition.Description,
                        Actions: plugins));
                }
            }
            Console.WriteLine($"Loaded {topics.Count} active topics from {definitions.Count} agent definitions.");  
            return topics.ToArray();
        }

        private static AgentDefinition? LoadDefinition(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
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

        private static Dictionary<string, KernelPlugin> BuildPluginCatalog()
        {
            return new Dictionary<string, KernelPlugin>(StringComparer.OrdinalIgnoreCase)
            {
                ["DatabaseTools"] = KernelPluginFactory.CreateFromObject(new DatabasePlugin(), "DatabaseTools"),
                ["VerificationPlugin"] = KernelPluginFactory.CreateFromObject(new VerificationPlugin(new ConversationState()), "VerificationPlugin"),
                ["NavigationPlugin"] = KernelPluginFactory.CreateFromObject(new NavigationPlugin(), "NavigationPlugin"),
                ["InappropriateContentGuardrail"] = KernelPluginFactory.CreateFromObject(new InappropriateContentGuardrail(), "InappropriateContentGuardrail"),
                ["PromptInjectionGuardrail"] = KernelPluginFactory.CreateFromObject(new PromptInjectionGuardrail(), "PromptInjectionGuardrail"),
                ["SampleGreetingPlugin"] = KernelPluginFactory.CreateFromObject(new SampleGreetingPlugin(), "SampleGreetingPlugin"),
                ["AstrologerPlugin"] = KernelPluginFactory.CreateFromObject(new AstrologerPlugin(), "AstrologerPlugin")
            };
        }

        private static IReadOnlyList<KernelPlugin> ResolvePlugins(
            IEnumerable<string> functionNames,
            IReadOnlyDictionary<string, KernelPlugin> catalog)
        {
            var pluginNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var functionName in functionNames ?? Enumerable.Empty<string>())
            {
                var normalized = functionName?.Trim();
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                var pluginName = normalized.Split('.', 2)[0];
                if (catalog.ContainsKey(pluginName))
                {
                    pluginNames.Add(pluginName);
                }
            }

            return pluginNames.Select(name => catalog[name]).ToArray();
        }
    }
}
