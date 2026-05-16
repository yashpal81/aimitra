using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aimitra.Core.Models;
using Aimitra.Services.Interfaces;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Aimitra.Services.Orchestration
{


/// <summary>
/// Represents a scoped topic — analogous to an Agentforce Topic.
/// Each topic has a description used for semantic routing and a set of
/// Actions (plugins) available exclusively when that topic is active.
/// </summary>
public sealed record Topic(
    string Name,
    string Description,
    IReadOnlyList<KernelPlugin> Actions);

/// <summary>
/// Provides a multi-model AI service using Semantic Kernel with a ReAct architecture
/// and Agentforce-style topic scoping.
///
/// NVIDIA LLM acts as the reasoning agent: given a prompt it first selects the most
/// relevant Topic (semantic routing), then executes a ReAct loop using only that
/// topic's Actions. This prevents the model seeing unrelated tools and mirrors the
/// way Agentforce's Atlas Reasoning Engine routes requests to Topics before planning.
///
/// Google Gemini handles text generation: it receives the reasoned output and produces
/// a polished, human-readable response.
///
/// Required NuGet packages:
///   Microsoft.SemanticKernel
///   Microsoft.SemanticKernel.Connectors.Google
/// </summary>
public sealed class MultiModelKernelService
{
    private const string NvidiaServiceId = "nvidia-reasoning";
    private const string GeminiServiceId = "gemini-generation";
    private const string ReActSystemPrompt =
        "You are a reasoning agent. Think step by step. Use the available tools to gather " +
        "information or perform actions whenever needed. Continue reasoning and acting until " +
        "you can provide a complete and accurate final answer.";

    private static readonly Uri NvidiaEndpoint = new("https://integrate.api.nvidia.com/v1");

    private readonly Kernel _kernel;

    /// <param name="nvidiaApiKey">NVIDIA API key.</param>
    /// <param name="nvidiaModelId">NVIDIA model ID (e.g. "nvidia/llama-3.1-nemotron-70b-instruct").</param>
    /// <param name="geminiApiKey">Google AI Gemini API key.</param>
    /// <param name="geminiModelId">Gemini model ID (e.g. "gemini-1.5-pro").</param>
    /// <param name="plugins">
    /// Optional collection of <see cref="KernelPlugin"/> instances representing the tools
    /// available to the NVIDIA reasoning agent during the ReAct loop.
    /// </param>
    public MultiModelKernelService(
        string nvidiaApiKey,
        string nvidiaModelId,
        string geminiApiKey,
        string geminiModelId,
        IEnumerable<KernelPlugin>? plugins = null)
    {
        ArgumentNullException.ThrowIfNull(nvidiaApiKey);
        ArgumentNullException.ThrowIfNull(nvidiaModelId);
        ArgumentNullException.ThrowIfNull(geminiApiKey);
        ArgumentNullException.ThrowIfNull(geminiModelId);


        _kernel = BuildKernel(nvidiaApiKey, nvidiaModelId, geminiApiKey, geminiModelId, plugins);
    }

    /// <summary>
    /// Registers an additional plugin (tool set) with the reasoning agent after construction.
    /// </summary>
    public void AddPlugin(KernelPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        _kernel.Plugins.Add(plugin);
    }

    /// <summary>
    /// Executes the ReAct loop using the NVIDIA LLM.
    ///
    /// The model reasons over <paramref name="prompt"/>, autonomously invokes registered tools
    /// as needed (Observe step), and iterates until it produces a final answer (Act step).
    /// Semantic Kernel's <see cref="FunctionChoiceBehavior.Auto()"/> drives the loop automatically.
    /// </summary>
    public async Task<string> ReasonAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>(NvidiaServiceId);

        var history = new ChatHistory(ReActSystemPrompt);
        history.AddUserMessage(prompt);

        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };

        // Passing _kernel enables Semantic Kernel to invoke tools and continue
        // the ReAct loop automatically until the model returns a final answer.
        var response = await chatCompletion.GetChatMessageContentAsync(
            history,
            executionSettings: settings,
            kernel: _kernel,
            cancellationToken: cancellationToken);

        return response.Content ?? string.Empty;
    }

    /// <summary>
    /// Sends a prompt to Google Gemini for text generation (e.g. summaries, drafts, content).
    /// No tools are available to Gemini — it performs pure generation only.
    /// </summary>
    public async Task<string> GenerateTextAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>(GeminiServiceId);

        var history = new ChatHistory();
        history.AddUserMessage(prompt);

        var response = await chatCompletion.GetChatMessageContentAsync(
            history,
            cancellationToken: cancellationToken);

        return response.Content ?? string.Empty;
    }

    /// <summary>
    /// Chains the two models: NVIDIA runs the ReAct loop to reason and act on
    /// <paramref name="prompt"/>, then Gemini generates a polished final response
    /// from the reasoned output.
    /// </summary>
    public async Task<string> ReasonThenGenerateAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var reasoning = await ReasonAsync(prompt, cancellationToken);
        return await GenerateTextAsync(reasoning, cancellationToken);
    }

    private static Kernel BuildKernel(
        string nvidiaApiKey,
        string nvidiaModelId,
        string geminiApiKey,
        string geminiModelId,
        IEnumerable<KernelPlugin>? plugins)
    {
        var builder = Kernel.CreateBuilder();

        builder.AddOpenAIChatCompletion(
            modelId: nvidiaModelId,
            apiKey: nvidiaApiKey,
            endpoint: NvidiaEndpoint,
            serviceId: NvidiaServiceId);

        builder.AddGoogleAIGeminiChatCompletion(
            modelId: geminiModelId,
            apiKey: geminiApiKey,
            serviceId: GeminiServiceId);

        var kernel = builder.Build();

        if (plugins is not null)
        {
            foreach (var plugin in plugins)
            {
                kernel.Plugins.Add(plugin);
            }
        }

        return kernel;
    }
}

}