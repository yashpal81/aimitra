
using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;


namespace Aimitra.Security{
public class PiiMaskingEngine : IFunctionInvocationFilter, IAutoFunctionInvocationFilter
{
    // Thread-safe dictionary storing Token -> Real Value mapping
    private readonly ConcurrentDictionary<string, string> _vault = new();
    
    // Core regex patterns to detect raw data coming from your DB tools
    private static readonly Regex EmailRegex = new(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled);
    private static readonly Regex SsnRegex = new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled);

    // =================================================================
    // STEP A: UNMASK BEFORE TOOL CALLS (LLM -> Tool)
    // =================================================================
    public async Task OnAutoFunctionInvocationAsync(AutoFunctionInvocationContext context, Func<AutoFunctionInvocationContext, Task> next)
    {
        // Iterate through all parameters the LLM is passing into your C# tool
        foreach (var argumentName in context.Arguments.Names)
        {
            var argumentValue = context.Arguments[argumentName]?.ToString();
            if (string.IsNullOrEmpty(argumentValue)) continue;

            // Scan the string for any placeholder tokens we created earlier
            foreach (var pair in _vault)
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

        // Let the C# function execute with the clean, real data safely restored
        await next(context);
    }

    // =================================================================
    // STEP B: MASK AFTER TOOL CALLS (Tool -> LLM)
    // =================================================================
    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        // Let the actual database or tool function execute first
        await next(context);

        var originalResult = context.Result.GetValue<string>();
        if (string.IsNullOrEmpty(originalResult)) return;

        // Process Emails
        string maskedResult = EmailRegex.Replace(originalResult, match =>
        {
            string rawValue = match.Value;
            string token = $"[MASKED_EMAIL_{Guid.NewGuid().ToString()[..4].ToUpper()}]";
            _vault[token] = rawValue; // Cache real value
            return token;
        });

        // Process SSNs
        maskedResult = SsnRegex.Replace(maskedResult, match =>
        {
            string rawValue = match.Value;
            string token = $"[MASKED_SSN_{Guid.NewGuid().ToString()[..4].ToUpper()}]";
            _vault[token] = rawValue; // Cache real value
            return token;
        });

        // Overwrite the payload so Gemini only sees the tracking token string
        context.Result = new FunctionResult(context.Function, maskedResult);
    }
}
}
