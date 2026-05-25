using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Aimitra.Security.Guardrails
{
    /// <summary>
    /// Convenience facade that registers both guardrails with a Semantic Kernel builder.
    ///
    /// Usage — in SemanticKernelOrchestrator (or any kernel setup):
    ///   GuardrailService.Register(builder, blockThreshold: 0.5);
    ///   var kernel = builder.Build();
    ///   kernel.Plugins.AddFromObject(GuardrailService.CreatePlugin(), "Guardrails");
    ///
    /// This matches the trace pattern where both guardrails appear in every agent's
    /// enabled_tools / tools_sent list, and also fire automatically as prompt filters.
    /// </summary>
    public static class GuardrailService
    {
        /// <summary>
        /// Registers InappropriateContentGuardrail and PromptInjectionGuardrail at all
        /// three Semantic Kernel filter points so they run automatically on every pipeline event:
        ///   • IPromptRenderFilter          — before every LLM call (checks the rendered prompt)
        ///   • IAutoFunctionInvocationFilter — before every auto-invoked function call (checks arguments)
        ///
        /// Call this once during kernel setup; no further opt-in is required.
        /// </summary>
        public static IKernelBuilder Register(
            IKernelBuilder builder,
            double blockThreshold = 0.5)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));

            var contentGuardrail   = new InappropriateContentGuardrail(blockThreshold);
            var injectionGuardrail = new PromptInjectionGuardrail(blockThreshold);

            // IPromptRenderFilter — fires before every LLM prompt
            builder.Services.AddSingleton<IPromptRenderFilter>(contentGuardrail);
            builder.Services.AddSingleton<IPromptRenderFilter>(injectionGuardrail);

            // IAutoFunctionInvocationFilter — fires before every auto-invoked kernel function
#pragma warning disable SKEXP0001
            builder.Services.AddSingleton<IAutoFunctionInvocationFilter>(contentGuardrail);
            builder.Services.AddSingleton<IAutoFunctionInvocationFilter>(injectionGuardrail);
#pragma warning restore SKEXP0001

            return builder;
        }

        /// <summary>
        /// Creates a KernelPlugin exposing both guardrails as callable LLM tools.
        /// Add this to the kernel after building so the LLM can invoke them explicitly
        /// (matching the tools_sent entries "Inappropriate_Content" and "Prompt_Injection"
        /// seen in every agent step of the trace).
        /// </summary>
        public static KernelPlugin CreatePlugin(double blockThreshold = 0.5)
        {
            var contentGuardrail   = new InappropriateContentGuardrail(blockThreshold);
            var injectionGuardrail = new PromptInjectionGuardrail(blockThreshold);

            // Merge both into one plugin named "Guardrails"
            var contentPlugin   = KernelPluginFactory.CreateFromObject(contentGuardrail,   "Guardrails");
            var injectionPlugin = KernelPluginFactory.CreateFromObject(injectionGuardrail, "Guardrails");

            return KernelPluginFactory.CreateFromFunctions("Guardrails",
                description: "Safety guardrails — inappropriate content detection and prompt injection detection.",
                functions: new[]
                {
                    contentPlugin["Inappropriate_Content"],
                    injectionPlugin["Prompt_Injection"]
                });
        }
    }
}
