using System.Collections.Concurrent;
using StevensSupportHelper.Server.Options;

namespace StevensSupportHelper.Server.Services;

public sealed class RequestRateLimitService
{
    private readonly ConcurrentDictionary<string, RequestRateLimitBucket> _buckets = new(StringComparer.Ordinal);

    public bool TryAcquire(string policyName, string partitionKey, RateLimitPolicyOptions options, out TimeSpan retryAfter)
    {
        ArgumentNullException.ThrowIfNull(policyName);
        ArgumentNullException.ThrowIfNull(partitionKey);
        ArgumentNullException.ThrowIfNull(options);

        var now = DateTimeOffset.UtcNow;
        var effectiveWindow = TimeSpan.FromSeconds(Math.Max(1, options.WindowSeconds));
        var effectivePermitLimit = Math.Max(1, options.PermitLimit);
        var bucket = _buckets.GetOrAdd($"{policyName}:{partitionKey}", _ => new RequestRateLimitBucket(now, 0));

        lock (bucket.SyncRoot)
        {
            if (now - bucket.WindowStartedAtUtc >= effectiveWindow)
            {
                bucket.WindowStartedAtUtc = now;
                bucket.Count = 0;
            }

            if (bucket.Count >= effectivePermitLimit)
            {
                retryAfter = effectiveWindow - (now - bucket.WindowStartedAtUtc);
                if (retryAfter < TimeSpan.Zero)
                {
                    retryAfter = TimeSpan.Zero;
                }

                return false;
            }

            bucket.Count++;
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    private sealed class RequestRateLimitBucket(DateTimeOffset windowStartedAtUtc, int count)
    {
        public object SyncRoot { get; } = new();
        public DateTimeOffset WindowStartedAtUtc { get; set; } = windowStartedAtUtc;
        public int Count { get; set; } = count;
    }
}
