using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace Aimitra.SamplePlugins.Plugins
{
    public sealed class SampleGreetingPlugin
    {
        [KernelFunction, Description("Returns a friendly greeting for the provided name.")]
        public string Greet(string name)
        {
            return $"Hello, {name}. This function was loaded from a plugin assembly.";
        }
    }
}
