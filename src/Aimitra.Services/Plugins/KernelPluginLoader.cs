using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.SemanticKernel;

namespace Aimitra.Services.Plugins
{
    public sealed class KernelPluginLoader
    {
        private readonly KernelPluginOptions _options;

        public KernelPluginLoader(KernelPluginOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

/// <summary>
/// Scans and registers kernel plugins based on the configured assembly paths. This method should be called during kernel setup to ensure all plugins are available for function execution. The loader will attempt to load each specified assembly, identify valid plugin classes, and register them with the kernel under a friendly name derived from the class name. Any issues during loading or registration will be logged to the console, but will not throw exceptions, allowing the kernel to operate with any successfully loaded plugins.
/// </summary>
/// <param name="kernel"></param>
/// <exception cref="ArgumentNullException"></exception>
        public void RegisterConfiguredPlugins(Kernel kernel)
        {
            if (kernel == null)
            {
                throw new ArgumentNullException(nameof(kernel));
            }

            foreach (var assemblyPath in _options.AssemblyPaths)
            {
                RegisterAssembly(kernel, assemblyPath);
            }
        }
        /// <summary>
        /// Loads a .NET assembly from the specified path and registers any classes that contain methods marked with the [KernelFunction] attribute as plugins in the provided kernel instance. The method supports both individual DLL files and directories containing multiple DLLs. For each valid plugin class found, an instance is created and registered under a name derived from the class name (with "Plugin" suffix removed if present). Any errors encountered during loading or registration are logged to the console, but do not halt the process, allowing for partial plugin availability if some assemblies fail to load.
        /// </summary>
        /// <param name="kernel"></param>
        /// <param name="assemblyPath"></param>

        private static void RegisterAssembly(Kernel kernel, string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                return;
            }

            var fullPath = Path.GetFullPath(assemblyPath);
            if (Directory.Exists(fullPath))
            {
                foreach (var dllPath in Directory.EnumerateFiles(fullPath, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    RegisterAssembly(kernel, dllPath);
                }

                return;
            }

            if (!File.Exists(fullPath))
            {
                Console.WriteLine($"Configured plugin assembly not found: {fullPath}");
                return;
            }

            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(fullPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load plugin assembly '{fullPath}': {ex.Message}");
                return;
            }

            foreach (var type in GetCandidateTypes(assembly))
            {
                RegisterType(kernel, type);
            }
        }

/// <summary>
/// Gets the types from the specified assembly that are candidates for plugin registration.
/// </summary>
/// <param name="assembly"></param>
/// <returns></returns>
        private static IEnumerable<Type> GetCandidateTypes(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(type => type != null).ToArray()!;
            }

            return types.Where(IsKernelPluginType);
        }

/// <summary>
/// Determines if a given type is a valid kernel plugin class by checking if it is a non-abstract class that contains at least one public instance method decorated with the [KernelFunction] attribute. This method is used to filter types during assembly scanning to identify which classes should be registered as plugins in the kernel.
/// </summary>
/// <param name="type"></param>
/// <returns></returns>
        private static bool IsKernelPluginType(Type type)
        {
            if (type == null || !type.IsClass || type.IsAbstract)
            {
                return false;
            }

            return type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(method => method.GetCustomAttributes().Any(attribute => attribute.GetType().Name == "KernelFunctionAttribute"));
        }

/// <summary>
/// Creates an instance of the specified plugin type and registers it with the kernel under a friendly name derived from the class name. The method attempts to instantiate the plugin class using its parameterless constructor, and if successful, adds it to the kernel's plugin collection. The registration name is determined by taking the class name and removing a "Plugin" suffix if it exists, allowing for cleaner plugin names in the kernel. Any exceptions during instantiation or registration are caught and logged to the console, ensuring that one faulty plugin does not prevent others from being registered.
/// </summary>
/// <param name="kernel"></param>
/// <param name="type"></param>
        private static void RegisterType(Kernel kernel, Type type)
        {
            try
            {
                var instance = Activator.CreateInstance(type);
                if (instance == null)
                {
                    Console.WriteLine($"Skipped plugin type '{type.FullName}' because it could not be instantiated.");
                    return;
                }

                var pluginName = type.Name.EndsWith("Plugin", StringComparison.OrdinalIgnoreCase)
                    ? type.Name[..^"Plugin".Length]
                    : type.Name;

                kernel.Plugins.AddFromObject(instance, pluginName);
                Console.WriteLine($"Registered kernel plugin '{pluginName}' from '{type.FullName}'.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to register plugin type '{type.FullName}': {ex.Message}");
            }
        }
    }
}
