using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Trivio.Filters
{
    /// <summary>
    /// Simple sliding-window rate limiter for hub invocations, backed by Redis.
    /// Limits each user/room/method to a small number of calls per second.
    /// </summary>
    public class RateLimitFilter : IHubFilter
    {
        private readonly IDatabase _database;
        private readonly ILogger<RateLimitFilter> _logger;

        // Tune these to adjust the limit behavior.
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(1);
        private const int MaxHitsPerWindow = 3;
        private static readonly TimeSpan KeyTtl = TimeSpan.FromSeconds(5);

        public RateLimitFilter(IDatabase database, ILogger<RateLimitFilter> logger)
        {
            _database = database;
            _logger = logger;
        }

        public async ValueTask<object?> InvokeMethodAsync(
            HubInvocationContext invocationContext,
            Func<HubInvocationContext, ValueTask<object?>> next)
        {
            // Identify caller and scope the key.
            var userId = invocationContext.Context.User?.FindFirst("userId")?.Value
                         ?? invocationContext.Context.ConnectionId
                         ?? "anonymous";
            var method = invocationContext.HubMethodName ?? "unknown";
            var roomSegment = GetRoomSegment(invocationContext);
            var key = $"ratelimit:{method}:{roomSegment}:{userId}";

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var cutoff = nowMs - (long)Window.TotalMilliseconds;

            try
            {
                await _database.SortedSetAddAsync(key, nowMs.ToString(), nowMs, When.Always);
                await _database.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, cutoff);
                var count = await _database.SortedSetLengthAsync(key);
                await _database.KeyExpireAsync(key, KeyTtl);

                if (count > MaxHitsPerWindow)
                {
                    _logger.LogWarning(
                        "Rate limit exceeded for user {UserId} on {Method} {RoomSegment}: {Count} hits in {WindowMs}ms",
                        userId, method, roomSegment, count, Window.TotalMilliseconds);
                    throw new HubException("Too many requests, please slow down.");
                }
            }
            catch (HubException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Fail-open with a warning to avoid blocking the method if Redis is unavailable.
                _logger.LogWarning(ex, "RateLimitFilter failed for user {UserId} on {Method}", userId, method);
            }

            return await next(invocationContext);
        }

        private static string GetRoomSegment(HubInvocationContext ctx)
        {
            if (ctx.HubMethodArguments.Count > 0 && ctx.HubMethodArguments[0] is int roomCode)
            {
                return roomCode.ToString();
            }

            return "no-room";
        }
    }
}
