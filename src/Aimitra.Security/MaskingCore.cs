
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

public class PresidioResponse
{
    public string Entity { get; set; } = string.Empty;
    public int Start { get; set; }
    public int End { get; set; }
}
public class MaskingCore 
{
    // Thread-safe dictionary storing Token -> Real Value mapping
    private readonly ConcurrentDictionary<string, string> _vault = new();

    public ConcurrentDictionary<string, string> Vault => _vault;    
    // Simulated delay of 30 seconds to run under LLM time constraints and observe masking/unmasking in action
    private readonly int waitingTime = 30000;
    // // Core regex patterns to detect raw data coming from your DB tools
    // private static readonly Regex EmailRegex = new(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled);
    // private static readonly Regex SsnRegex = new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled);


//======================================================
     string originalResult= "Hello, my name is Yashpal Sharma and my email id is yashpal.sharma@gmail.com";   
     HttpClient _httpClient = new();
        
    public MaskingCore(string presidioEndpoint)
    {
        _httpClient.BaseAddress = new Uri(presidioEndpoint);
    }

// =================================================================
    // STEP 0: MASKING IN THE PROMPT (LLM -> LLM)
    // =================================================================
    public async Task<string> maskPiiData(string prompt)
    {
        Console.WriteLine("Prompt before to Call LLM masking: " + prompt);
        await Task.Delay(waitingTime);
        if (string.IsNullOrEmpty(prompt)) return prompt;

        var response = await _httpClient.PostAsJsonAsync("scan", new { text = prompt });
        if (!response.IsSuccessStatusCode) return prompt;

        var detections = await response.Content.ReadFromJsonAsync<PresidioResponse[]>();
        if (detections == null || detections.Length == 0) return prompt;

        // Process entities backwards from end to start so indexing positions don't shift!
        var sb = new StringBuilder(prompt);
        Array.Sort(detections, (a, b) => b.Start.CompareTo(a.Start));

        foreach (var pii in detections)
        {
            string token = $"[MASKED_{pii.Entity}_{Guid.NewGuid().ToString()[..4].ToUpper()}]";
            
            // Optional: Save this token to your private dictionary vault here if unmasking later
            string rawValue = prompt.Substring(pii.Start, pii.End - pii.Start);
            sb.Remove(pii.Start, pii.End - pii.Start);
            sb.Insert(pii.Start, token);
            _vault[token] = rawValue;
        }
        string maskedPrompt = sb.ToString();
        Console.WriteLine("Masked Prompt before calling LLM: " + maskedPrompt);
        return maskedPrompt;
    }
// =================================================================
    // STEP A: UNMASK BEFORE TOOL CALLS (LLM -> Tool)
    // =================================================================
    public async Task<string> unmaskPiiData(string result)
    {
        Console.WriteLine("Original Result before unmasking: " + result);
        if (string.IsNullOrEmpty(result)) return result;

        foreach (var pair in _vault)
        {
            if (result.Contains(pair.Key))
            {
                // Restore the real data back into the tool parameters
                result = result.Replace(pair.Key, pair.Value);
            }
        }
        Console.WriteLine("Unmasked Result for Tool: " + result);
        return result;
    }


    }
}
