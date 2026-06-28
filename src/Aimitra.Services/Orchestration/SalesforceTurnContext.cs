using System;
using System.Threading;

namespace METASYNAPSE.Services.Orchestration
{
    public sealed class SalesforceTurnContext
    {
        private static readonly AsyncLocal<SalesforceTurnContext?> CurrentContext = new();

        public static SalesforceTurnContext? Current
        {
            get => CurrentContext.Value;
            set => CurrentContext.Value = value;
        }

        public string? UserId { get; init; }
        public string? Email { get; init; }
        public string? AccessToken { get; init; }
    }
}
