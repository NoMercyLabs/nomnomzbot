// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>
/// Sends the playback director one <see cref="DuckStart"/> when bot audio starts speaking and one
/// <see cref="DuckEnd"/> when the last queued line has finished. Each channel keeps the moment its speech
/// ends; a new line starts counting from that moment when it is later than now.
/// </summary>
public sealed class MusicDuckScheduler(
    IPlaybackInputSink sink,
    TimeProvider time,
    ILogger<MusicDuckScheduler> logger
) : IMusicDuckScheduler, IDisposable
{
    private sealed class Duck(int percent, DateTimeOffset until, ITimer timer)
    {
        public int Percent { get; set; } = percent;
        public DateTimeOffset Until { get; set; } = until;
        public ITimer Timer { get; } = timer;
    }

    private readonly ConcurrentDictionary<Guid, Duck> _ducks = new();
    private readonly object _gate = new();

    public async Task DuckAsync(
        Guid broadcasterId,
        int percent,
        int durationMs,
        CancellationToken ct = default
    )
    {
        if (percent >= 100 || durationMs <= 0)
            return;

        int clamped = Math.Max(percent, 0);
        bool send;
        lock (_gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            if (_ducks.TryGetValue(broadcasterId, out Duck? active))
            {
                send = active.Percent != clamped;
                active.Percent = clamped;
                active.Until = (active.Until > now ? active.Until : now).AddMilliseconds(
                    durationMs
                );
                active.Timer.Change(active.Until - now, Timeout.InfiniteTimeSpan);
            }
            else
            {
                send = true;
                DateTimeOffset until = now.AddMilliseconds(durationMs);
                ITimer timer = time.CreateTimer(
                    _ => EndDuck(broadcasterId),
                    null,
                    until - now,
                    Timeout.InfiniteTimeSpan
                );
                _ducks[broadcasterId] = new(clamped, until, timer);
            }
        }

        if (send)
            await sink.SendAsync(broadcasterId, new DuckStart(clamped), ct);
    }

    private void EndDuck(Guid broadcasterId)
    {
        lock (_gate)
        {
            if (!_ducks.TryGetValue(broadcasterId, out Duck? active))
                return;
            if (active.Until > time.GetUtcNow())
                return;

            _ducks.TryRemove(broadcasterId, out _);
            active.Timer.Dispose();
        }

        _ = SendDuckEndAsync(broadcasterId);
    }

    private async Task SendDuckEndAsync(Guid broadcasterId)
    {
        try
        {
            await sink.SendAsync(broadcasterId, new DuckEnd());
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not restore the music level after bot audio for channel {BroadcasterId}.",
                broadcasterId
            );
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (Duck duck in _ducks.Values)
                duck.Timer.Dispose();
            _ducks.Clear();
        }
    }
}
