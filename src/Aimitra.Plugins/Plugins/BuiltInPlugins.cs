using Microsoft.SemanticKernel.Plugins.Core;

namespace Aimitra.Plugins.Plugins;

public static class BuiltInPlugins
{
    public static IReadOnlyDictionary<string, object> Create()
    {
        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["ConversationSummaryPlugin"] = new ConversationSummaryPlugin(),
            ["TimePlugin"] = new TimePlugin(),
            ["TextPlugin"] = new TextPlugin(),
            ["MathPlugin"] = new MathPlugin(),
            ["FileIOPlugin"] = new FileIOPlugin(),
            ["HttpPlugin"] = new HttpPlugin(),
            ["WaitPlugin"] = new WaitPlugin()
        };
    }
}
