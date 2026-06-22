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
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Resolves a message's badges to image urls from the cached Helix badge sets (chat-decoration spec §3.3). It builds a
/// (set id, version id) → urls lookup from the global set then the channel set — so a channel's own version overrides the
/// global on a collision — and maps each badge through it. Cache-only: an un-warmed badge resolves to empty urls (the
/// client falls back), never dropped.
/// </summary>
public sealed class ChatBadgeResolver : IChatBadgeResolver
{
    private static readonly IReadOnlyDictionary<string, string> NoUrls =
        new Dictionary<string, string>();

    private readonly ICacheService _cache;

    public ChatBadgeResolver(ICacheService cache)
    {
        _cache = cache;
    }

    public async Task<IReadOnlyList<ResolvedChatBadge>> ResolveAsync(
        Guid broadcasterId,
        IReadOnlyList<ChatBadge> badges,
        CancellationToken ct = default
    )
    {
        if (badges.Count == 0)
            return [];

        Dictionary<(string Set, string Version), IReadOnlyDictionary<string, string>> index = new();
        await IndexSetsAsync(index, ChatBadgeCacheKeys.Global, ct);
        await IndexSetsAsync(index, ChatBadgeCacheKeys.Channel(broadcasterId), ct);

        List<ResolvedChatBadge> resolved = new(badges.Count);
        foreach (ChatBadge badge in badges)
        {
            IReadOnlyDictionary<string, string> urls = index.TryGetValue(
                (badge.SetId, badge.Id),
                out IReadOnlyDictionary<string, string>? found
            )
                ? found
                : NoUrls;
            resolved.Add(new ResolvedChatBadge(badge.SetId, badge.Id, badge.Info, urls));
        }

        return resolved;
    }

    private async Task IndexSetsAsync(
        Dictionary<(string Set, string Version), IReadOnlyDictionary<string, string>> index,
        string cacheKey,
        CancellationToken ct
    )
    {
        IReadOnlyList<TwitchChatBadgeSet>? sets = await _cache.GetAsync<
            IReadOnlyList<TwitchChatBadgeSet>
        >(cacheKey, ct);
        if (sets is null)
            return;

        foreach (TwitchChatBadgeSet set in sets)
        foreach (TwitchChatBadgeVersion version in set.Versions)
            index[(set.SetId, version.Id)] = new Dictionary<string, string>
            {
                ["1"] = version.ImageUrl1x,
                ["2"] = version.ImageUrl2x,
                ["4"] = version.ImageUrl4x,
            };
    }
}
