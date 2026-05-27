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

            Console.WriteLine($"Finished loading agent definitions. {definitions.Count} active definitions found.");
            if (definitions.Count == 0)
            {
                return Array.Empty<Topic>();
            }

            var pluginCatalog = BuildActivePluginCatalog();
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
            var pluginSources = BuildPluginSources();
            Console.WriteLine($"Building list of available kernel functions from plugin source map with {pluginSources.Count} plugins.");
            foreach (var (pluginName, pluginSource) in pluginSources)
            {
                Console.WriteLine($"Processing plugin '{pluginName}' for kernel function options.");
                foreach (var function in EnumeratePluginFunctions(pluginSource))
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
                        FunctionType = "Native",
                        Description = GetFunctionDescription(function)
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

        public string GetWelcomeMessage()
        {
            var definitions = LoadActiveAgentDefinitions();
            var welcomeMessage = definitions
                .Select(definition => definition.WelcomeMessage?.Trim())
                .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));

            return string.IsNullOrWhiteSpace(welcomeMessage)
                ? "Welcome to Aimitra. Start a conversation and I’ll route it through the right tools, knowledge base, and guardrails."
                : welcomeMessage!;
        }

        private static IReadOnlyList<KernelFunctionOption> LoadPromptKernelFunctionOptions()
        {
            var results = new List<KernelFunctionOption>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                    var functionKey = $"{pluginName}.{functionName}";
                    if (!seen.Add(functionKey))
                    {
                        continue;
                    }

                    results.Add(new KernelFunctionOption
                    {
                        DisplayName = functionName,
                        FullName = functionKey,
                        Group = pluginName,
                        FunctionType = "Prompt",
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


        private static Dictionary<string, KernelPlugin> BuildActivePluginCatalog()
        {
            var activeDefinitions = LoadActiveAgentDefinitions();
            var requiredPluginNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var definition in activeDefinitions)
            {
                foreach (var topic in definition.Topics)
                {
                    foreach (var functionName in topic.KernelFunctions ?? Enumerable.Empty<string>())
                    {
                        var normalized = functionName?.Trim();
                        if (string.IsNullOrWhiteSpace(normalized))
                        {
                            continue;
                        }

                        requiredPluginNames.Add(normalized.Split('.', 2)[0]);
                    }
                }
            }

            var pluginCatalog = new Dictionary<string, KernelPlugin>(StringComparer.OrdinalIgnoreCase);
            var codePlugins = BuildCodePluginCatalog();
            foreach (var pluginName in requiredPluginNames)
            {
                if (codePlugins.TryGetValue(pluginName, out var plugin))
                {
                    pluginCatalog[pluginName] = plugin;
                }
            }

            foreach (var kvp in LoadPluginsForTopic())
            {
                if (requiredPluginNames.Count == 0 || requiredPluginNames.Contains(kvp.Key))
                {
                    pluginCatalog[kvp.Key] = kvp.Value;
                }
            }

            Console.WriteLine($"Plugin catalog built with {pluginCatalog.Count} active plugins from {activeDefinitions.Count} active definitions.");
            return pluginCatalog;
        }

        private static IReadOnlyList<AgentDefinition> LoadActiveAgentDefinitions()
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "App_Data", "agent-definitions");
            if (!Directory.Exists(folder))
            {
                return Array.Empty<AgentDefinition>();
            }

            return Directory.EnumerateFiles(folder, "*.json")
                .Select(LoadDefinition)
                .Where(definition => definition is not null && definition.Active)
                .Select(definition => definition!)
                .ToArray();
        }

        private static Dictionary<string, KernelPlugin> BuildCodePluginCatalog()
        {
            return new Dictionary<string, KernelPlugin>(StringComparer.OrdinalIgnoreCase)
            {
                ["DatabaseTools"] = KernelPluginFactory.CreateFromObject(new DatabasePlugin(), "DatabaseTools"),
                ["VerificationPlugin"] = KernelPluginFactory.CreateFromObject(new VerificationPlugin(new ConversationState()), "VerificationPlugin"),
                ["NavigationPlugin"] = KernelPluginFactory.CreateFromObject(new NavigationPlugin(), "NavigationPlugin"),
                ["InappropriateContentGuardrail"] = KernelPluginFactory.CreateFromObject(new InappropriateContentGuardrail(), "InappropriateContentGuardrail"),
                ["PromptInjectionGuardrail"] = KernelPluginFactory.CreateFromObject(new PromptInjectionGuardrail(), "PromptInjectionGuardrail"),
                ["SampleGreetingPlugin"] = KernelPluginFactory.CreateFromObject(new SampleGreetingPlugin(), "SampleGreetingPlugin"),
                ["AstrologerPlugin"] = KernelPluginFactory.CreateFromObject(new AstrologerPlugin(), "AstrologerPlugin"),
                ["KnowledgeBasePlugin"] = KernelPluginFactory.CreateFromObject(new KnowledgeBasePlugin(new DocumentMemoryService()), "KnowledgeBasePlugin")
            };
        }

        private static Dictionary<string, object> BuildPluginSources()
        {
            return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["DatabaseTools"] = new DatabasePlugin(),
                ["VerificationPlugin"] = new VerificationPlugin(new ConversationState()),
                ["NavigationPlugin"] = new NavigationPlugin(),
                ["InappropriateContentGuardrail"] = new InappropriateContentGuardrail(),
                ["PromptInjectionGuardrail"] = new PromptInjectionGuardrail(),
                ["SampleGreetingPlugin"] = new SampleGreetingPlugin(),
                ["AstrologerPlugin"] = new AstrologerPlugin(),
                ["KnowledgeBasePlugin"] = new KnowledgeBasePlugin(new DocumentMemoryService())
            };
        }

        public static Dictionary<string, KernelPlugin> LoadPluginsForTopic()
        {
            var pluginfunctions = new List<KernelFunction>();
            var seenFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                    var functionKey = $"{pluginName}.{functionName}";
                    if (!seenFunctions.Add(functionKey))
                    {
                        continue;
                    }
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
            var pluginType = plugin.GetType();
            var functionsProperty = pluginType.GetProperty("Functions");
            Console.WriteLine($"Enumerating functions for plugin of type '{pluginType.Name}'. Functions property found: {functionsProperty is not null}");

            if (functionsProperty?.GetValue(plugin) is System.Collections.IEnumerable functions)
            {
                Console.WriteLine("Functions property is enumerable. Enumerating functions...");
                foreach (var function in functions)
                {
                    if (function is not null)
                    {
                        Console.WriteLine($"Yielding function of type '{function.GetType().Name}' from Functions property.");
                        yield return function;
                    }
                }
                yield break;
            }

            Console.WriteLine($"Falling back to public instance methods decorated with [KernelFunction] on '{pluginType.Name}'.");
            foreach (var method in pluginType.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            {
                var hasKernelFunctionAttribute = method.GetCustomAttributes(inherit: true)
                    .Any(attribute => attribute.GetType().Name == "KernelFunctionAttribute");

                if (!hasKernelFunctionAttribute)
                {
                    continue;
                }

                Console.WriteLine($"Yielding kernel function method '{method.Name}' from plugin type '{pluginType.Name}'.");
                yield return method;
            }
        }

        private static string? GetStringProperty(object instance, string propertyName)
        {
            return instance.GetType().GetProperty(propertyName)?.GetValue(instance)?.ToString();
        }

        private static string GetFunctionDescription(object function)
        {
            var functionType = function.GetType();

            if (function is System.Reflection.MethodInfo methodInfo)
            {
                var descriptionAttribute = methodInfo
                    .GetCustomAttributes(inherit: true)
                    .OfType<System.ComponentModel.DescriptionAttribute>()
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(descriptionAttribute?.Description))
                {
                    return descriptionAttribute.Description;
                }

                var kernelDescriptionAttribute = methodInfo
                    .GetCustomAttributes(inherit: true)
                    .FirstOrDefault(attribute => attribute.GetType().Name == "KernelFunctionAttribute");

                var kernelDescription = kernelDescriptionAttribute?.GetType().GetProperty("Description")?.GetValue(kernelDescriptionAttribute)?.ToString();
                if (!string.IsNullOrWhiteSpace(kernelDescription))
                {
                    return kernelDescription;
                }
            }

            var descriptionProperty = functionType.GetProperty("Description");
            return descriptionProperty?.GetValue(function)?.ToString() ?? string.Empty;
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
