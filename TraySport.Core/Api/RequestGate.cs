namespace TraySport.Core.Api;

/// <summary>
/// Sliding-window rate limiter: at most <c>limit</c> requests start per <c>window</c>. The free
/// API allows 30 requests a minute and blocks the caller for a minute after that, so the client
/// stays under it by waiting instead of getting locked out.
/// </summary>
public sealed class RequestGate
{
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly TimeProvider _time;
    private readonly Queue<DateTimeOffset> _starts = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;

    public RequestGate(int limit, TimeSpan window, TimeProvider? time = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        _limit = limit;
        _window = window;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// How long the next request must wait, given the start times already in the window. Pure, so
    /// the policy is testable without a clock.
    /// </summary>
    public static TimeSpan DelayFor(IReadOnlyCollection<DateTimeOffset> startsInWindow, int limit, TimeSpan window, DateTimeOffset now, DateTimeOffset blockedUntil)
    {
        TimeSpan delay = blockedUntil > now ? blockedUntil - now : TimeSpan.Zero;
        if (startsInWindow.Count >= limit)
        {
            // Wait until the oldest start that keeps us at the limit falls out of the window.
            DateTimeOffset frees = startsInWindow.OrderBy(t => t).ElementAt(startsInWindow.Count - limit) + window;
            if (frees - now > delay)
            {
                delay = frees - now;
            }
        }

        return delay;
    }

    /// <summary>The server said slow down: hold every request until <paramref name="until"/>.</summary>
    public void BlockUntil(DateTimeOffset until)
    {
        if (until > _blockedUntil)
        {
            _blockedUntil = until;
        }
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                DateTimeOffset now = _time.GetUtcNow();
                while (_starts.Count > 0 && _starts.Peek() + _window <= now)
                {
                    _starts.Dequeue();
                }

                TimeSpan delay = DelayFor(_starts, _limit, _window, now, _blockedUntil);
                if (delay <= TimeSpan.Zero)
                {
                    _starts.Enqueue(now);
                    return;
                }

                await Task.Delay(delay, _time, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}
