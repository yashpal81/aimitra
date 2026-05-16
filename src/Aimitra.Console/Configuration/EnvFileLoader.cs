using System;
using System.Collections.Generic;
using System.IO;

namespace Aimitra.ConsoleApp.Configuration
{
    internal static class EnvFileLoader
    {
        public static void Load(string? environmentName)
        {
            foreach (var path in GetCandidatePaths(environmentName))
            {
                if (File.Exists(path))
                {
                    LoadFile(path);
                    return;
                }
            }
        }

        private static IEnumerable<string> GetCandidatePaths(string? environmentName)
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            var normalizedEnvironment = environmentName?.Trim().ToLowerInvariant();

            while (current != null)
            {
                if (normalizedEnvironment == "production")
                {
                    yield return Path.Combine(current.FullName, ".env.production");
                    yield return Path.Combine(current.FullName, ".env.local");
                }
                else if (normalizedEnvironment == "local")
                {
                    yield return Path.Combine(current.FullName, ".env.local");
                    yield return Path.Combine(current.FullName, ".env.production");
                }
                else
                {
                    yield return Path.Combine(current.FullName, ".env.local");
                    yield return Path.Combine(current.FullName, ".env.production");
                }

                yield return Path.Combine(current.FullName, ".env");

                current = current.Parent;
            }
        }

        private static void LoadFile(string path)
        {
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                var key = line[..equalsIndex].Trim();
                var value = line[(equalsIndex + 1)..].Trim().Trim('"');

                if (!string.IsNullOrWhiteSpace(key) && Environment.GetEnvironmentVariable(key) is null)
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }
        }
    }
}
