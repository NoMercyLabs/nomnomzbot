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
