// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Resolves a cheermote to its tier image from the cached Helix cheermotes (chat-decoration spec §3.4). It matches the
/// cheermote by prefix (case-insensitive), then picks the tier the cheer qualified for — the highest <c>MinBits</c> not
/// exceeding the bits cheered (cheermote images step up with the amount), falling back to the lowest tier — and returns
/// that tier's dark-theme animated image plus its colour. Cache-only: a miss or unknown prefix returns null.
/// </summary>
public sealed class ChatCheermoteResolver : ICheermoteResolver
{
    private readonly ICacheService _cache;

    public ChatCheermoteResolver(ICacheService cache)
    {
        _cache = cache;
    }

    public async Task<CheermoteImage?> ResolveAsync(
        Guid broadcasterId,
        string prefix,
        int bits,
        int tier,
        CancellationToken ct = default
    )
    {
        IReadOnlyList<TwitchCheermote>? cheermotes = await _cache.GetAsync<
            IReadOnlyList<TwitchCheermote>
        >(ChatCheermoteCacheKeys.Channel(broadcasterId), ct);
        if (cheermotes is null)
            return null;

        TwitchCheermote? cheermote = cheermotes.FirstOrDefault(candidate =>
            string.Equals(candidate.Prefix, prefix, StringComparison.OrdinalIgnoreCase)
        );
        if (cheermote is null || cheermote.Tiers.Count == 0)
            return null;

        TwitchCheermoteTier matched =
            cheermote
                .Tiers.Where(candidate => candidate.MinBits <= bits)
                .OrderByDescending(candidate => candidate.MinBits)
                .FirstOrDefault()
            ?? cheermote.Tiers.OrderBy(candidate => candidate.MinBits).First();

        return new CheermoteImage(
            matched.Images.Dark.Animated.Scales,
            Animated: true,
            matched.Color
        );
    }
}
