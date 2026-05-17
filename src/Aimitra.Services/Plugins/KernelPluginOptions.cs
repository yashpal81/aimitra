using System;
using System.Collections.Generic;
using System.Linq;

namespace Aimitra.Services.Plugins
{
    public sealed class KernelPluginOptions
    {
        public IReadOnlyList<string> AssemblyPaths { get; }

        public KernelPluginOptions(IReadOnlyList<string> assemblyPaths)
        {
            AssemblyPaths = assemblyPaths ?? Array.Empty<string>();
        }

        public static KernelPluginOptions FromEnvironment()
        {
            var rawPaths = Environment.GetEnvironmentVariable("AIMITRA_KERNEL_PLUGIN_ASSEMBLIES");
            if (string.IsNullOrWhiteSpace(rawPaths))
            {
                return new KernelPluginOptions(Array.Empty<string>());
            }

            var paths = rawPaths
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();

            return new KernelPluginOptions(paths);
        }
    }
}
