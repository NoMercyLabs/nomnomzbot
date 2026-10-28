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
/// Singleton <see cref="ITtsChannelSerializer"/> — one <see cref="SemaphoreSlim"/>(1,1) per channel,
/// created lazily and kept for the process lifetime (a channel dispatches TTS occasionally at most, so
/// the small per-channel memory footprint is not worth the complexity of eviction).
/// </summary>
public sealed class TtsChannelSerializer : ITtsChannelSerializer
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    public async Task<IAsyncDisposable> AcquireAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        SemaphoreSlim gate = _gates.GetOrAdd(broadcasterId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Releaser(gate);
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly SemaphoreSlim _gate;

        public Releaser(SemaphoreSlim gate) => _gate = gate;

        public ValueTask DisposeAsync()
        {
            _gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
