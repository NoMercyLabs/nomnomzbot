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

namespace NomNomzBot.Infrastructure.Chat.YouTube;

/// <summary>
/// Process-wide (channel, stickerId) → our own channel asset URL memo. Kept as its own singleton, separate
/// from <see cref="YouTubeSuperStickerAssetResolver"/> (scoped — it uses the scoped <c>IChannelAssetService</c>
/// / <c>IApplicationDbContext</c>), so a fresh scope per poll tick never loses the memo: only the DATA is
/// process-wide, the resolve LOGIC still runs with a correctly-scoped DbContext each time.
/// </summary>
public sealed class YouTubeSuperStickerAssetCache
{
    private readonly ConcurrentDictionary<(Guid BroadcasterId, string StickerId), string> _urls =
        new();

    public bool TryGet(Guid broadcasterId, string stickerId, out string? assetUrl) =>
        _urls.TryGetValue((broadcasterId, stickerId), out assetUrl);

    public void Set(Guid broadcasterId, string stickerId, string assetUrl) =>
        _urls[(broadcasterId, stickerId)] = assetUrl;
}
