using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace METASYNAPSE.Services.RateLimiting
{
    public static class LlmRateLimiter
    {
        private static readonly object Sync = new();
        private static readonly Queue<DateTimeOffset> RequestTimestamps = new();
        private const int MaxRequestsPerMinute = 2;

        /// <summary>
        /// Waits until the next LLM request may be issued under the configured
        /// rate limit. This is a global, sliding-window limiter across the app.
        /// </summary>
        public static async Task WaitForAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            while (true)
            {
                TimeSpan delay;
                lock (Sync)
                {
                    var now = DateTimeOffset.UtcNow;
                    while (RequestTimestamps.Count > 0 && now - RequestTimestamps.Peek() >= TimeSpan.FromMinutes(1))
                    {
                        RequestTimestamps.Dequeue();
                    }

                    if (RequestTimestamps.Count < MaxRequestsPerMinute)
                    {
                        RequestTimestamps.Enqueue(now);
                        return;
                    }

                    delay = TimeSpan.FromMinutes(1) - (now - RequestTimestamps.Peek());
                    if (delay < TimeSpan.Zero)
                    {
                        delay = TimeSpan.Zero;
                    }
                }

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await Task.Yield();
                }
            }
        }
    }
}
