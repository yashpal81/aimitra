
using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Numerics;

namespace Aimitra.Security{

public class PiiMaskingEngine : IPromptRenderFilter, IFunctionInvocationFilter, IAutoFunctionInvocationFilter
{
    // Thread-safe dictionary storing Token -> Real Value mapping
    //private readonly ConcurrentDictionary<string, string> _vault = new();
    
    private readonly MaskingCore _maskingCore;
    // Simulated delay of 30 seconds to run under LLM time constraints and observe masking/unmasking in action
    private readonly int waitingTime = 30000;
    // // Core regex patterns to detect raw data coming from your DB tools
    // private static readonly Regex EmailRegex = new(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled);
    // private static readonly Regex SsnRegex = new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled);


//======================================================
     string originalResult= "Hello, my name is Yashpal Sharma and my email id is yashpal.sharma@gmail.com";   
     HttpClient _httpClient = new();
        
    public PiiMaskingEngine(string presidioEndpoint)
    {
        _httpClient.BaseAddress = new Uri(presidioEndpoint);
        _maskingCore = new MaskingCore(presidioEndpoint);
    }

// =================================================================
    // STEP 0: MASKING IN THE PROMPT (LLM -> LLM)
    // =================================================================
    public async Task<string> maskPrompt(string prompt)
    {
        Console.WriteLine("Prompt before to Call LLM masking: " + prompt);
        string maskedPrompt= await _maskingCore.maskPiiData(prompt);
        Console.WriteLine("Masked Prompt before calling LLM: " + maskedPrompt);
        return maskedPrompt;
    }
// =================================================================
    // STEP A: UNMASK BEFORE TOOL CALLS (LLM -> Tool)
    // =================================================================
    public async Task<string> unmaskResult(string result)
    {
        Console.WriteLine("Original Result before unmasking: " + result);
        string unmaskedResult= await _maskingCore.unmaskPiiData(result);
        Console.WriteLine("Unmasked Result for Tool: " + unmaskedResult);
        return unmaskedResult;
    }


// =================================================================
// STEP 0: MASKING IN THE PROMPT (LLM -> LLM)
// =================================================================
    public async Task OnPromptRenderAsync(PromptRenderContext context, Func<PromptRenderContext , Task> next)
    {
        Console.WriteLine("Prompt before to Call LLM masking: " + context.RenderedPrompt);
        await Task.Delay(waitingTime);
        // Only process the main user input prompt, not system or assistant messages
        if (context.RenderedPrompt != null)// && context.RenderedPrompt.Role == "user")
        {
            string prompt = context.RenderedPrompt;
            string maskedPrompt = await _maskingCore.maskPiiData(prompt);
        Console.WriteLine("Masked Prompt: " + maskedPrompt);
        // Overwrite the prompt so Gemini only sees the tracking token string
        context.RenderedPrompt = maskedPrompt;
        }
        Console.WriteLine("Prompt after masking to call LLM: " + context.RenderedPrompt);
        await next(context);
    }   

    // =================================================================
    // STEP A: UNMASK BEFORE TOOL CALLS (LLM -> Tool)
    // =================================================================
    public async Task OnAutoFunctionInvocationAsync(AutoFunctionInvocationContext context, Func<AutoFunctionInvocationContext, Task> next)
    {
        await Task.Delay(waitingTime);
        // Iterate through all parameters the LLM is passing into your C# tool
        foreach (var argumentName in context.Arguments.Names)
        {
            var argumentValue = context.Arguments[argumentName]?.ToString();
            if (string.IsNullOrEmpty(argumentValue)) continue;

            // Scan the string for any placeholder tokens we created earlier
            foreach (var pair in _maskingCore.Vault)
            {
                if (argumentValue.Contains(pair.Key))
                {
                    // Restore the real data back into the tool parameters
                    argumentValue = argumentValue.Replace(pair.Key, pair.Value);
                }
            }

            // Update the live execution argument
            context.Arguments[argumentName] = argumentValue;
        }
        Console.WriteLine("Unmasked Arguments for Tool:");
        foreach (var argumentName in context.Arguments.Names)        {
            Console.WriteLine($"{argumentName}: {context.Arguments[argumentName]}");
        }
        // Let the C# function execute with the clean, real data safely restored
        await next(context);
    }

    // =================================================================
    // STEP B: MASK AFTER TOOL CALLS (Tool -> LLM)
    // =================================================================
    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
       await Task.Delay(waitingTime);
        // Let the actual database or tool function execute first
        await next(context);

        var originalResult = context.Result.GetValue<string>();
        if (string.IsNullOrEmpty(originalResult)) return;

        // Process Emails
        // string maskedResult = EmailRegex.Replace(originalResult, match =>
        // {
        //     string rawValue = match.Value;
        //     string token = $"[MASKED_EMAIL_{Guid.NewGuid().ToString()[..4].ToUpper()}]";
        //     _vault[token] = rawValue; // Cache real value
        //     return token;
        // });

        // // Process SSNs
        // maskedResult = SsnRegex.Replace(maskedResult, match =>
        // {
        //     string rawValue = match.Value;
        //     string token = $"[MASKED_SSN_{Guid.NewGuid().ToString()[..4].ToUpper()}]";
        //     _vault[token] = rawValue; // Cache real value
        //     return token;
        // });

        var response = await _httpClient.PostAsJsonAsync("scan", new { text = originalResult });
        if (!response.IsSuccessStatusCode) return;

        var detections = await response.Content.ReadFromJsonAsync<PresidioResponse[]>();
        if (detections == null || detections.Length == 0) return;

        // Process entities backwards from end to start so indexing positions don't shift!
        var sb = new StringBuilder(originalResult);
        Array.Sort(detections, (a, b) => b.Start.CompareTo(a.Start));

        foreach (var pii in detections)
        {
            string token = $"[MASKED_{pii.Entity}_{Guid.NewGuid().ToString()[..4].ToUpper()}]";
            
            // Optional: Save this token to your private dictionary vault here if unmasking later
            string rawValue = originalResult.Substring(pii.Start, pii.End - pii.Start);
            sb.Remove(pii.Start, pii.End - pii.Start);
            sb.Insert(pii.Start, token);
            _maskingCore.Vault[token] = rawValue;
        }

        string maskedResult = sb.ToString();
        Console.WriteLine("Original Result: " + originalResult);
        Console.WriteLine("Masked Result: " + maskedResult);
        // Overwrite the payload so Gemini only sees the tracking token string
        context.Result = new FunctionResult(context.Function, maskedResult);
    }
}
}
