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
using NomNomzBot.Application.Contracts.Tts;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// Singleton <see cref="ITtsSceneReservation"/> — one active reservation per channel, held in memory for
/// the process lifetime (see the interface doc for why a reservation exists and how the token is used).
/// </summary>
public sealed class TtsSceneReservation : ITtsSceneReservation
{
    private readonly ConcurrentDictionary<Guid, Entry> _active = new();
    private readonly TimeProvider _clock;

    public TtsSceneReservation(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public Task<string> ReserveAsync(
        Guid broadcasterId,
        TimeSpan maxHold,
        CancellationToken ct = default
    )
    {
        string token = Guid.CreateVersion7().ToString("n");
        Entry entry = new(token, _clock.GetUtcNow() + maxHold, new TaskCompletionSource());
        _active[broadcasterId] = entry;
        return Task.FromResult(token);
    }

    public Task ReleaseAsync(Guid broadcasterId, string token, CancellationToken ct = default)
    {
        if (
            _active.TryGetValue(broadcasterId, out Entry? entry)
            && entry.Token == token
            && _active.TryRemove(new KeyValuePair<Guid, Entry>(broadcasterId, entry))
        )
            entry.Released.TrySetResult();
        return Task.CompletedTask;
    }

    public async Task WaitForClearAsync(
        Guid broadcasterId,
        string? callerToken,
        CancellationToken ct = default
    )
    {
        while (true)
        {
            if (!_active.TryGetValue(broadcasterId, out Entry? entry))
                return;

            DateTimeOffset now = _clock.GetUtcNow();
            if (entry.Token == callerToken)
                return;
            if (now >= entry.ExpiresAt)
            {
                _active.TryRemove(new KeyValuePair<Guid, Entry>(broadcasterId, entry));
                continue;
            }

            TimeSpan remaining = entry.ExpiresAt - now;
            using CancellationTokenSource timeout = new(remaining, _clock);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                ct,
                timeout.Token
            );
            try
            {
                await entry.Released.Task.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Either released (loop re-checks and returns) or expired (loop re-checks and clears it).
            }
        }
    }

    private sealed record Entry(
        string Token,
        DateTimeOffset ExpiresAt,
        TaskCompletionSource Released
    );
}
