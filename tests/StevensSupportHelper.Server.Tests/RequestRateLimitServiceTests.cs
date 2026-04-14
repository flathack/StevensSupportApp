using StevensSupportHelper.Server.Options;
using StevensSupportHelper.Server.Services;

namespace StevensSupportHelper.Server.Tests;

public sealed class RequestRateLimitServiceTests
{
    [Fact]
    public void TryAcquire_WithinPermitLimit_ReturnsTrue()
    {
        var service = new RequestRateLimitService();
        var options = new RateLimitPolicyOptions
        {
            PermitLimit = 2,
            WindowSeconds = 60
        };

        var firstAttempt = service.TryAcquire("admin-read", "partition-a", options, out var firstRetryAfter);
        var secondAttempt = service.TryAcquire("admin-read", "partition-a", options, out var secondRetryAfter);

        Assert.True(firstAttempt);
        Assert.True(secondAttempt);
        Assert.Equal(TimeSpan.Zero, firstRetryAfter);
        Assert.Equal(TimeSpan.Zero, secondRetryAfter);
    }

    [Fact]
    public void TryAcquire_ExceedingPermitLimit_ReturnsFalseAndRetryAfter()
    {
        var service = new RequestRateLimitService();
        var options = new RateLimitPolicyOptions
        {
            PermitLimit = 1,
            WindowSeconds = 60
        };

        var firstAttempt = service.TryAcquire("client", "partition-a", options, out _);
        var secondAttempt = service.TryAcquire("client", "partition-a", options, out var retryAfter);

        Assert.True(firstAttempt);
        Assert.False(secondAttempt);
        Assert.True(retryAfter > TimeSpan.Zero);
        Assert.True(retryAfter <= TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void TryAcquire_DifferentPartitions_DoNotShareBudget()
    {
        var service = new RequestRateLimitService();
        var options = new RateLimitPolicyOptions
        {
            PermitLimit = 1,
            WindowSeconds = 60
        };

        var firstPartitionAttempt = service.TryAcquire("client", "partition-a", options, out _);
        var secondPartitionAttempt = service.TryAcquire("client", "partition-b", options, out _);

        Assert.True(firstPartitionAttempt);
        Assert.True(secondPartitionAttempt);
    }
}
