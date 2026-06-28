using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using METASYNAPSE.Services.Orchestration;

namespace METASYNAPSE.Services.Plugins
{
    public class SalesforcePlugin
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        [KernelFunction("Get_Salesforce_Account_IDs_using_SOQL_Query")]
        [Description("Uses Salesforce SOQL query semantics to return Account Ids using a Salesforce sessionId.")]
        public async Task<string> GetAccountIdsSOQLQueryApexAsync(
            // string? instanceUrl = null,
            // string? sessionId = null,
            // string? accessToken = null,
            // string? sf_user_id = null,
            // string? sf_user_email = null,
            // string? soqlFilter = null,
            int limit = 100)
        {
            Console.WriteLine("Invoked SalesforcePlugin.GetAccountIdsSOQLQueryApex with parameters:");
            // Console.WriteLine($"instanceUrl: {instanceUrl}");
            // Console.WriteLine($"sessionId: {sessionId}");
            // Console.WriteLine($"accessToken: {accessToken}");
            // Console.WriteLine($"sf_user_id: {sf_user_id}");
            // Console.WriteLine($"sf_user_email: {sf_user_email}");
            // Console.WriteLine($"soqlFilter: {soqlFilter}");
            Console.WriteLine($"limit: {limit}");

        
            var turnContext = SalesforceTurnContext.Current;
            if (turnContext is not null)
            {
                Console.WriteLine($"turnContext.UserId: {turnContext.UserId}");
                Console.WriteLine($"turnContext.Email: {turnContext.Email}");
                Console.WriteLine($"turnContext.AccessToken present: {!string.IsNullOrWhiteSpace(turnContext.AccessToken)}");
            }

            //return "Account burlington, Account san francisco , Account new york";
            var instanceUrl = Environment.GetEnvironmentVariable("SALESFORCE_INSTANCE_URL");
            //sessionId ??= Environment.GetEnvironmentVariable("SALESFORCE_SESSION_ID");
            var accessToken = turnContext.AccessToken;//Environment.GetEnvironmentVariable("SALESFORCE_ACCESS_TOKEN");
            var apiVersion = Environment.GetEnvironmentVariable("SALESFORCE_API_VERSION")?.Trim() ?? "58.0";

            if (string.IsNullOrWhiteSpace(instanceUrl))
            {
                return "Salesforce instance URL is missing. Set SALESFORCE_INSTANCE_URL or pass the instanceUrl parameter.";
            }

            var token = accessToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                return "Salesforce sessionId or access token is missing. Pass sessionId, accessToken, or set SALESFORCE_SESSION_ID/SALESFORCE_ACCESS_TOKEN.";
            }

            var auditContext = string.Join(", ", new[]
            {
                string.IsNullOrWhiteSpace(turnContext.UserId) ? null : $"sf_user_id={turnContext.UserId}",
                string.IsNullOrWhiteSpace(turnContext.Email) ? null : $"sf_user_email={turnContext.Email}"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

            var trimmedInstanceUrl = instanceUrl.TrimEnd('/');
            var query = BuildSoql("", limit);

            try
            {
                var requestUrl = $"{trimmedInstanceUrl}/services/data/v{apiVersion}/query?q={Uri.EscapeDataString(query)}";
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                Console.WriteLine($"turnContext.AccessToken present: {!string.IsNullOrWhiteSpace(turnContext.AccessToken)}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
Console.WriteLine($"Token; {token}");
Console.WriteLine($"Salesforce response status: {response.StatusCode}");
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                        response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        return $"It seems there is an issue with the Salesforce session—it is either expired or invalid. Please ensure you are authenticated correctly so I can retrieve the Account IDs for you.{response.StatusCode}: {content}";
                    }

                    return $"Salesforce query failed with status {response.StatusCode}: {content}";
                }

                using var document = JsonDocument.Parse(content);
                if (!document.RootElement.TryGetProperty("records", out var records))
                {
                    return $"Salesforce response did not contain records. Raw response: {content}";
                }

                var ids = new List<string>();
                foreach (var record in records.EnumerateArray())
                {
                    if (record.TryGetProperty("Id", out var idProperty))
                    {
                        ids.Add(idProperty.GetString() ?? string.Empty);
                    }
                }

                var apexSnippet = BuildAnonymousApexSnippet(query);
                return JsonSerializer.Serialize(new
                {
                    auditContext,
                    query,
                    accountIds = ids,
                    anonymousApex = apexSnippet
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return $"Salesforce request failed: {ex.Message}";
            }
        }


        [KernelFunction("Get_Salesforce_Data_Using_Dynamically_Built_SOQL_Query")]
        [Description("User can retrieve Salesforce data using a dynamically built SOQL query.")]
        public async Task<string> GetSalesforceOrgDataUsingSOQLQueryApexAsync(
            string soqlQuery,
            int limit = 10)
        {
            Console.WriteLine("Invoked SalesforcePlugin.GetSalesforceOrgDataUsingSOQLQueryApex with parameters:");
            // Console.WriteLine($"instanceUrl: {instanceUrl}");
            // Console.WriteLine($"sessionId: {sessionId}");
            // Console.WriteLine($"accessToken: {accessToken}");
            // Console.WriteLine($"sf_user_id: {sf_user_id}");
            // Console.WriteLine($"sf_user_email: {sf_user_email}");
             Console.WriteLine($"soqlQuery: {soqlQuery}");
            Console.WriteLine($"limit: {limit}");

        
            var turnContext = SalesforceTurnContext.Current;
            if (turnContext is not null)
            {
                Console.WriteLine($"turnContext.UserId: {turnContext.UserId}");
                Console.WriteLine($"turnContext.Email: {turnContext.Email}");
                Console.WriteLine($"turnContext.AccessToken present: {!string.IsNullOrWhiteSpace(turnContext.AccessToken)}");
            }

            //return "Account burlington, Account san francisco , Account new york";
            var instanceUrl = Environment.GetEnvironmentVariable("SALESFORCE_INSTANCE_URL");
            //sessionId ??= Environment.GetEnvironmentVariable("SALESFORCE_SESSION_ID");
            var accessToken = turnContext.AccessToken;//Environment.GetEnvironmentVariable("SALESFORCE_ACCESS_TOKEN");
            var apiVersion = Environment.GetEnvironmentVariable("SALESFORCE_API_VERSION")?.Trim() ?? "58.0";

            if (string.IsNullOrWhiteSpace(instanceUrl))
            {
                return "Salesforce instance URL is missing. Set SALESFORCE_INSTANCE_URL or pass the instanceUrl parameter.";
            }

            var token = accessToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                return "Salesforce sessionId or access token is missing. Pass sessionId, accessToken, or set SALESFORCE_SESSION_ID/SALESFORCE_ACCESS_TOKEN.";
            }

            var auditContext = string.Join(", ", new[]
            {
                string.IsNullOrWhiteSpace(turnContext.UserId) ? null : $"sf_user_id={turnContext.UserId}",
                string.IsNullOrWhiteSpace(turnContext.Email) ? null : $"sf_user_email={turnContext.Email}"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

            var trimmedInstanceUrl = instanceUrl.TrimEnd('/');
            var query = soqlQuery ?? BuildSoql("", limit);

            try
            {
                var requestUrl = $"{trimmedInstanceUrl}/services/data/v{apiVersion}/query?q={Uri.EscapeDataString(query)}";
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                Console.WriteLine($"turnContext.AccessToken present: {!string.IsNullOrWhiteSpace(turnContext.AccessToken)}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
Console.WriteLine($"Token; {token}");
Console.WriteLine($"Salesforce response status: {response.StatusCode}");
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                        response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        return $"It seems there is an issue with the Salesforce session—it is either expired or invalid. Please ensure you are authenticated correctly so I can retrieve the Account IDs for you.{response.StatusCode}: {content}";
                    }

                    return $"Salesforce query failed with status {response.StatusCode}: {content}";
                }

                using var document = JsonDocument.Parse(content);
                if (!document.RootElement.TryGetProperty("records", out var records))
                {
                    return $"Salesforce response did not contain records. Raw response: {content}";
                }

                // var names = new List<string>();
                // foreach (var record in records.EnumerateArray())
                // {
                //     if (record.TryGetProperty("Name", out var nameProperty))
                //     {
                //         names.Add(nameProperty.GetString() ?? string.Empty);
                //     }
                // }

                var apexSnippet = BuildAnonymousApexSnippet(query);
                return JsonSerializer.Serialize(new
                {
                    auditContext,
                    query,
                    records = records.EnumerateArray().Select(r => r.ToString()).ToList()
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return $"Salesforce request failed: {ex.Message}";
            }
        }




        [KernelFunction("Execute_Salesforce_Anonymous_Apex")]
        [Description("Uses Salesforce anonymous Apex-style query semantics to return Account Ids using a Salesforce sessionId.")]
        public async Task<string> ExecuteAnonymousApexAsync(
            string? anonymousApex)
        {
            Console.WriteLine("Invoked SalesforcePlugin.GetAccountIdsUsingAnonymousApexAsync with parameters:");
            Console.WriteLine($"anonymousApex: {anonymousApex}");
        
            var turnContext = SalesforceTurnContext.Current;
            if (turnContext is not null)
            {
                Console.WriteLine($"turnContext.UserId: {turnContext.UserId}");
                Console.WriteLine($"turnContext.Email: {turnContext.Email}");
                Console.WriteLine($"turnContext.AccessToken present: {!string.IsNullOrWhiteSpace(turnContext.AccessToken)}");
            }

            //return "Account burlington, Account san francisco , Account new york";
            var instanceUrl = Environment.GetEnvironmentVariable("SALESFORCE_INSTANCE_URL");
            //sessionId ??= Environment.GetEnvironmentVariable("SALESFORCE_SESSION_ID");
            var accessToken = turnContext.AccessToken;//Environment.GetEnvironmentVariable("SALESFORCE_ACCESS_TOKEN");
            var apiVersion = Environment.GetEnvironmentVariable("SALESFORCE_API_VERSION")?.Trim() ?? "58.0";

            if (string.IsNullOrWhiteSpace(instanceUrl))
            {
                return "Salesforce instance URL is missing. Set SALESFORCE_INSTANCE_URL or pass the instanceUrl parameter.";
            }

            var token = accessToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                return "Salesforce sessionId or access token is missing. Pass sessionId, accessToken, or set SALESFORCE_SESSION_ID/SALESFORCE_ACCESS_TOKEN.";
            }

            var auditContext = string.Join(", ", new[]
            {
                string.IsNullOrWhiteSpace(turnContext.UserId) ? null : $"sf_user_id={turnContext.UserId}",
                string.IsNullOrWhiteSpace(turnContext.Email) ? null : $"sf_user_email={turnContext.Email}"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

            var trimmedInstanceUrl = instanceUrl.TrimEnd('/');
            var query = anonymousApex ?? "List<Account> accounts = Database.query(\"SELECT Id FROM Account LIMIT 5\");\n" +
                            "List<String> ids = new List<String>();\n" +
                            "for (Account a : accounts) ids.add(a.Id);\n" +
                            "System.debug('ACCOUNT_IDS:' + String.join(ids, ','));";

            try
            {
                var requestUrl = $"{trimmedInstanceUrl}/services/data/v60.0/tooling/executeAnonymous/?anonymousBody={Uri.EscapeDataString(query)}";
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                Console.WriteLine($"turnContext.AccessToken present: {!string.IsNullOrWhiteSpace(turnContext.AccessToken)}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
Console.WriteLine($"Token; {token}");
Console.WriteLine($"Salesforce response status: {response.StatusCode}");
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                        response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        return $"It seems there is an issue with the Salesforce session—it is either expired or invalid. Please ensure you are authenticated correctly so I can retrieve the Account IDs for you.{response.StatusCode}: {content}";
                    }

                    return $"Salesforce query failed with status {response.StatusCode}: {content}";
                }

                using var document = JsonDocument.Parse(content);
                if (!document.RootElement.TryGetProperty("records", out var records))
                {
                    return $"Salesforce response did not contain records. Raw response: {content}";
                }

                var ids = new List<string>();
                foreach (var record in records.EnumerateArray())
                {
                    if (record.TryGetProperty("Id", out var idProperty))
                    {
                        ids.Add(idProperty.GetString() ?? string.Empty);
                    }
                }

                var apexSnippet = BuildAnonymousApexSnippet(query);
                return JsonSerializer.Serialize(new
                {
                    auditContext,
                    query,
                    accountIds = ids,
                    anonymousApex = apexSnippet
                }, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                return $"Salesforce request failed: {ex.Message}";
            }
        }


        private static string BuildSoql(string? soqlFilter, int limit)
        {
            var filterClause = string.IsNullOrWhiteSpace(soqlFilter)
                ? string.Empty
                : $" WHERE {soqlFilter.Trim()}";

            return $"SELECT Id FROM Account{filterClause} LIMIT {Math.Clamp(limit, 1, 200)}";
        }

        private static string BuildAnonymousApexSnippet(string soql)
        {
            var escapedSoql = soql.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return $"List<Account> accounts = Database.query(\"{escapedSoql}\");\n" +
                   "List<String> ids = new List<String>();\n" +
                   "for (Account a : accounts) ids.add(a.Id);\n" +
                   "System.debug('ACCOUNT_IDS:' + String.join(ids, ','));";
        }
    }
}
