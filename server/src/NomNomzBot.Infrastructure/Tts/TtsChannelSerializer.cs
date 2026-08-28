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
