using System.ComponentModel;
using System.Text.Json;
using Aimitra.Core.Models;
using Microsoft.SemanticKernel;

namespace Aimitra.Services.Plugins
{
    /// <summary>
    /// Semantic Kernel plugin for the Verification agent.
    ///
    /// Simulates a CRM identity-check: accepts an account number and the last
    /// four digits of the customer's SSN, and on a successful match populates
    /// the shared <see cref="ConversationState"/> with the full customer profile.
    ///
    /// In production replace the stub look-up table with a real CRM / IAM call.
    /// </summary>
    public sealed class VerificationPlugin
    {
        private readonly ConversationState _state;

        /// <summary>
        /// Initialises the plugin with the shared session state.
        /// The plugin writes verified customer facts directly into <paramref name="state"/>
        /// so every subsequent agent hop has access without additional look-ups.
        /// </summary>
        public VerificationPlugin(ConversationState state)
        {
            _state = state ?? throw new System.ArgumentNullException(nameof(state));
        }

        /// <summary>
        /// Verifies the customer's identity.
        /// Returns a JSON object with <c>success</c>, <c>customerId</c>, and the
        /// full profile on success, or <c>success: false</c> with an <c>error</c>
        /// message on failure.
        /// </summary>
        [KernelFunction("verify_customer")]
        [Description(
            "Verifies a customer's identity using their account number and the last 4 digits " +
            "of their SSN. On success, returns the full customer profile and marks the session " +
            "as verified. Call go_back with nextTopic='topic_selector' after a successful " +
            "verification so the main router can proceed with the customer's original request.")]
        public string VerifyCustomer(
            [Description("The customer's account number (e.g. ACC-10042).")]
            string accountNumber,
            [Description("Last 4 digits of the customer's Social Security Number.")]
            string ssnLast4)
        {
            if (string.IsNullOrWhiteSpace(accountNumber) || string.IsNullOrWhiteSpace(ssnLast4))
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = "Both accountNumber and ssnLast4 are required to verify identity."
                });
            }

            // ── Simulated customer store ──────────────────────────────────────
            // Each entry: (accountNumber, ssnLast4) → customer profile
            // Replace with a real CRM call in production.
            var customers = new[]
            {
                new
                {
                    AccountNumber  = "ACC-10042",
                    SsnLast4       = "4832",
                    CustomerId     = "CUST-78A21",
                    CustomerName   = "Jane Doe",
                    Email          = "jane.doe@example.com",
                    Phone          = "555-0100",
                    ServicePlan    = "Fiber 500",
                    Address        = "123 Main St, Springfield, IL 62701"
                },
                new
                {
                    AccountNumber  = "ACC-20077",
                    SsnLast4       = "9921",
                    CustomerId     = "CUST-44B09",
                    CustomerName   = "John Smith",
                    Email          = "john.smith@example.com",
                    Phone          = "555-0199",
                    ServicePlan    = "Fiber 1000",
                    Address        = "456 Oak Ave, Shelbyville, IL 62565"
                }
            };

            foreach (var c in customers)
            {
                if (string.Equals(c.AccountNumber, accountNumber.Trim(), System.StringComparison.OrdinalIgnoreCase)
                    && c.SsnLast4 == ssnLast4.Trim())
                {
                    // Populate shared session state
                    _state.CustomerVerified = true;
                    _state.AccountNumber    = c.AccountNumber;
                    _state.CustomerId       = c.CustomerId;
                    _state.CustomerName     = c.CustomerName;
                    _state.CustomerEmail    = c.Email;
                    _state.PhoneNumber      = c.Phone;
                    _state.ServicePlan      = c.ServicePlan;
                    _state.AddressOnFile    = c.Address;

                    return JsonSerializer.Serialize(new
                    {
                        success      = true,
                        customerId   = c.CustomerId,
                        customerName = c.CustomerName,
                        email        = c.Email,
                        phone        = c.Phone,
                        servicePlan  = c.ServicePlan,
                        address      = c.Address
                    });
                }
            }

            // No match found
            return JsonSerializer.Serialize(new
            {
                success = false,
                error   = "We could not verify your identity with the provided account number and SSN. " +
                          "Please double-check the details and try again, or contact support."
            });
        }
    }
}
