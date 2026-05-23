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
            var folder = Path.Combine(AppContext.BaseDirectory, "App_Data", "agent-definitions");
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

        public IReadOnlyList<KernelFunctionOption> LoadAvailableKernelFunctions()
        {
            var options = new List<KernelFunctionOption>();
            var pluginCatalog = BuildPluginCatalog();
    Console.WriteLine($"Building list of available kernel functions from plugin catalog with {pluginCatalog.Count} plugins.");
            foreach (var (pluginName, plugin) in pluginCatalog)
            {
                Console.WriteLine($"Processing plugin '{pluginName}' for kernel function options.");
                foreach (var function in EnumeratePluginFunctions(plugin))
                {
                    Console.WriteLine($"Inspecting function in plugin '{pluginName}' for kernel function options.");
                    var functionName = GetStringProperty(function, "Name") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(functionName))
                    {
                        continue;
                    }
Console.WriteLine($"Adding kernel function option for function '{functionName}' in plugin '{pluginName}'.");
                    options.Add(new KernelFunctionOption
                    {
                        DisplayName = functionName,
                        FullName = $"{pluginName}.{functionName}",
                        Group = pluginName,
                        Description = GetStringProperty(function, "Description") ?? string.Empty
                    });
                }
            }

            options.AddRange(LoadPromptKernelFunctionOptions());
Console.WriteLine($"Total kernel function options loaded: {options.Count}");
Console.WriteLine("Kernel function options:");
            foreach (var option in options)            {
                Console.WriteLine($"- {option.FullName} (Display: {option.DisplayName}, Group: {option.Group})");
            }   
            
            return options
                .DistinctBy(option => option.FullName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(option => option.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IReadOnlyList<KernelFunctionOption> LoadPromptKernelFunctionOptions()
        {
            var results = new List<KernelFunctionOption>();

            foreach (var root in GetPromptPluginRoots().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var promptPath in Directory.EnumerateFiles(root, "skprompt.txt", SearchOption.AllDirectories))
                {
                    var functionDirectory = Path.GetDirectoryName(promptPath);
                    if (string.IsNullOrWhiteSpace(functionDirectory))
                    {
                        continue;
                    }

                    var functionName = Path.GetFileName(functionDirectory);
                    var pluginName = GetPromptPluginName(root, functionDirectory);

                    results.Add(new KernelFunctionOption
                    {
                        DisplayName = functionName,
                        FullName = $"{pluginName}.{functionName}",
                        Group = pluginName,
                        Description = ReadPromptDescription(Path.Combine(functionDirectory, "config.json"))
                    });
                }
            }

            return results;
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
            Dictionary<string, KernelPlugin> pluginCatalog = new Dictionary<string, KernelPlugin>(StringComparer.OrdinalIgnoreCase)
            {
                ["DatabaseTools"] = KernelPluginFactory.CreateFromObject(new DatabasePlugin(), "DatabaseTools"),
                ["VerificationPlugin"] = KernelPluginFactory.CreateFromObject(new VerificationPlugin(new ConversationState()), "VerificationPlugin"),
                ["NavigationPlugin"] = KernelPluginFactory.CreateFromObject(new NavigationPlugin(), "NavigationPlugin"),
                ["InappropriateContentGuardrail"] = KernelPluginFactory.CreateFromObject(new InappropriateContentGuardrail(), "InappropriateContentGuardrail"),
                ["PromptInjectionGuardrail"] = KernelPluginFactory.CreateFromObject(new PromptInjectionGuardrail(), "PromptInjectionGuardrail"),
                ["SampleGreetingPlugin"] = KernelPluginFactory.CreateFromObject(new SampleGreetingPlugin(), "SampleGreetingPlugin"),
                ["AstrologerPlugin"] = KernelPluginFactory.CreateFromObject(new AstrologerPlugin(), "AstrologerPlugin")
               
            };

            Dictionary<string, KernelPlugin> promptPlugins = LoadPluginsForTopic();
            foreach (var kvp in promptPlugins)
            {
                pluginCatalog[kvp.Key] = kvp.Value;
            }

            Console.WriteLine($"Plugin catalog built with {pluginCatalog.Count} total plugins, including {promptPlugins.Count} from prompts.");  
            return pluginCatalog;

        }

        public static Dictionary<string, KernelPlugin> LoadPluginsForTopic()
        {
            var pluginfunctions = new List<KernelFunction>();
            foreach (var root in GetPromptPluginRoots())
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var promptPath in Directory.EnumerateFiles(root, "skprompt.txt", SearchOption.AllDirectories))
                {
                    var functionDirectory = Path.GetDirectoryName(promptPath);
                    if (string.IsNullOrWhiteSpace(functionDirectory))
                    {
                        continue;
                    }

                    var functionName = Path.GetFileName(functionDirectory);
                    var pluginName = GetPromptPluginName(root, functionDirectory);
                    var configPath = Path.Combine(functionDirectory, "config.json");

                    var promptTemplate = File.ReadAllText(promptPath);

                    PromptTemplateConfig templateConfig;
                    if (File.Exists(configPath))
                    {
                        var configJson = File.ReadAllText(configPath);
                        templateConfig = JsonSerializer.Deserialize<PromptTemplateConfig>(configJson)
                                        ?? new PromptTemplateConfig();
                    }
                    else
                    {
                        templateConfig = new PromptTemplateConfig();
                    }

                    templateConfig.Template = promptTemplate;
                    templateConfig.Name = functionName;

                    KernelFunction kernelFunction = KernelFunctionFactory.CreateFromPrompt(templateConfig);
                    pluginfunctions.Add(kernelFunction);
                }
            }

            if (pluginfunctions.Count == 0)
            {
                return new Dictionary<string, KernelPlugin>();
            }

            return new Dictionary<string, KernelPlugin>
            {
                ["OfficePlugin"] = KernelPluginFactory.CreateFromFunctions("OfficePlugin", pluginfunctions)
            };
        }

        private static IEnumerable<string> GetPromptPluginRoots()
        {
            yield return Path.Combine(AppContext.BaseDirectory, "App_Data", "Plugins");
            yield return Path.Combine(_environmentFallback(), "App_Data", "Plugins");
            yield return Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "Plugins");
            yield return Path.Combine(GetProjectRootFallback(), "App_Data", "Plugins");
        }

        private static string _environmentFallback() => AppContext.BaseDirectory;

        private static string GetPromptPluginName(string root, string functionDirectory)
        {
            var relative = Path.GetRelativePath(root, functionDirectory);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 0 ? segments[0] : Path.GetFileName(functionDirectory);
        }

        private static string ReadPromptDescription(string configPath)
        {
            if (!File.Exists(configPath))
            {
                return string.Empty;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                return doc.RootElement.TryGetProperty("description", out var description)
                    ? description.GetString() ?? string.Empty
                    : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetProjectRootFallback()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "App_Data")))
                {
                    return dir.FullName;
                }
            }

            return AppContext.BaseDirectory;
        }

        private static IEnumerable<object> EnumeratePluginFunctions(object plugin)
        {
            var functionsProperty = plugin.GetType().GetProperty("Functions");
            if (functionsProperty?.GetValue(plugin) is System.Collections.IEnumerable functions)
            {
                foreach (var function in functions)
                {
                    if (function is not null)
                    {
                        yield return function;
                    }
                }
            }
        }

        private static string? GetStringProperty(object instance, string propertyName)
        {
            return instance.GetType().GetProperty(propertyName)?.GetValue(instance)?.ToString();
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
