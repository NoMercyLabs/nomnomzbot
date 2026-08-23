// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using NomNomzBot.Application.Contracts.Chat;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// <see cref="IChatSendQueue"/> (S010): a per-<c>queueKey</c> (channel+platform) token bucket paces
/// sends to the platform's rate limit instead of firing them blindly, and concurrent sends sharing the
/// same <c>coalesceKey</c> join a single in-flight send rather than each consuming a token and posting
/// the identical line twice. Singleton — the bucket state and in-flight map must outlive the scoped
/// <c>ChatPlatformRouter</c> that calls it per request. Never drops a send: every accepted call either
/// runs it (after waiting out the bucket) or joins another call's honest result — nothing is discarded.
/// </summary>
public sealed class TokenBucketChatSendQueue : IChatSendQueue
{
    private readonly int _capacity;
    private readonly TimeSpan _refillPeriod;
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _bucketLocks = new();
    private readonly ConcurrentDictionary<string, Task<bool>> _inFlight = new();

    /// <param name="capacity">Tokens the bucket holds — sends allowed in one burst before pacing kicks in.</param>
    /// <param name="refillPeriod">Time to refill the bucket from empty to <paramref name="capacity"/>.</param>
    public TokenBucketChatSendQueue(int capacity = 20, TimeSpan? refillPeriod = null)
    {
        _capacity = Math.Max(1, capacity);
        _refillPeriod =
            refillPeriod is { } period && period > TimeSpan.Zero
                ? period
                : TimeSpan.FromSeconds(30);
    }

    public async Task<bool> EnqueueAsync(
        string queueKey,
        string coalesceKey,
        Func<CancellationToken, Task<bool>> send,
        CancellationToken cancellationToken = default
    )
    {
        while (true)
        {
            if (_inFlight.TryGetValue(coalesceKey, out Task<bool>? joined))
                return await joined.ConfigureAwait(false);

            TaskCompletionSource<bool> tcs = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            if (!_inFlight.TryAdd(coalesceKey, tcs.Task))
                continue; // lost the registration race — loop back and join whoever won it

            try
            {
                await AcquireTokenAsync(queueKey, cancellationToken).ConfigureAwait(false);
                bool result = await send(cancellationToken).ConfigureAwait(false);
                tcs.SetResult(result);
                return result;
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
                throw;
            }
            finally
            {
                _inFlight.TryRemove(coalesceKey, out _);
            }
        }
    }

    private async Task AcquireTokenAsync(string queueKey, CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = _bucketLocks.GetOrAdd(queueKey, _ => new SemaphoreSlim(1, 1));
        double msPerToken = _refillPeriod.TotalMilliseconds / _capacity;

        while (true)
        {
            TimeSpan wait;
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Bucket bucket = _buckets.GetOrAdd(
                    queueKey,
                    _ => new Bucket { Tokens = _capacity, LastRefillTicks = DateTime.UtcNow.Ticks }
                );

                long nowTicks = DateTime.UtcNow.Ticks;
                double elapsedMs =
                    (nowTicks - bucket.LastRefillTicks) / (double)TimeSpan.TicksPerMillisecond;
                if (elapsedMs > 0)
                {
                    double refill = elapsedMs / msPerToken;
                    if (refill > 0)
                    {
                        bucket.Tokens = Math.Min(_capacity, bucket.Tokens + refill);
                        bucket.LastRefillTicks = nowTicks;
                    }
                }

                if (bucket.Tokens >= 1)
                {
                    bucket.Tokens -= 1;
                    return;
                }

                double needed = 1 - bucket.Tokens;
                wait = TimeSpan.FromMilliseconds(Math.Max(1, needed * msPerToken));
            }
            finally
            {
                gate.Release();
            }

            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class Bucket
    {
        public double Tokens;
        public long LastRefillTicks;
    }
}
