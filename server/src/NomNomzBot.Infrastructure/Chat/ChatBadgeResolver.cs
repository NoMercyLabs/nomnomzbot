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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat.Jobs;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Resolves a message's badges to image urls from the cached Helix badge sets (chat-decoration spec §3.3). It builds a
/// (set id, version id) → urls lookup from the global set then the channel set — so a channel's own version overrides the
/// global on a collision — and maps each badge through it. A missing channel set is warmed once on demand (read-through),
/// so an offline channel, which the refresh worker no longer covers, still resolves its own badges. A failed warm is not
/// retried for <see cref="RetryAfter"/> and concurrent callers share one in-flight warm. An un-warmed badge resolves to
/// empty urls (the client falls back), never dropped.
/// </summary>
public sealed class ChatBadgeResolver : IChatBadgeResolver
{
    private static readonly IReadOnlyDictionary<string, string> NoUrls =
        new Dictionary<string, string>();

    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private readonly ICacheService _cache;
    private readonly IServiceScopeFactory _scopes;
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _failedUntil = new();
    private readonly ConcurrentDictionary<Guid, Task> _inFlight = new();

    public ChatBadgeResolver(ICacheService cache, IServiceScopeFactory scopes)
    {
        _cache = cache;
        _scopes = scopes;
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
        await EnsureChannelSetAsync(broadcasterId, ct);
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
            resolved.Add(new(badge.SetId, badge.Id, badge.Info, urls));
        }

        return resolved;
    }

    private async Task EnsureChannelSetAsync(Guid broadcasterId, CancellationToken ct)
    {
        if (await _cache.ExistsAsync(ChatBadgeCacheKeys.Channel(broadcasterId), ct))
            return;
        if (
            _failedUntil.TryGetValue(broadcasterId, out DateTimeOffset until)
            && until > DateTimeOffset.UtcNow
        )
            return;

        Task warm = _inFlight.GetOrAdd(broadcasterId, WarmChannelAsync);
        await warm.WaitAsync(ct);
        _inFlight.TryRemove(new KeyValuePair<Guid, Task>(broadcasterId, warm));
    }

    private async Task WarmChannelAsync(Guid broadcasterId)
    {
        bool warmed;
        try
        {
            await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
            ChatBadgeCacheWarmer warmer =
                scope.ServiceProvider.GetRequiredService<ChatBadgeCacheWarmer>();
            warmed = await warmer.WarmChannelAsync(broadcasterId);
        }
        catch (Exception)
        {
            warmed = false;
        }

        if (warmed)
            _failedUntil.TryRemove(broadcasterId, out _);
        else
            _failedUntil[broadcasterId] = DateTimeOffset.UtcNow + RetryAfter;
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
        {
            // Only keep sizes that actually resolved to a url. A cached version can carry null/empty image
            // urls (a partially-populated Helix payload deserialized into the non-null string fields); emitting
            // {"1":null,...} would serialize null values into a Dictionary<string,string> and break the client's
            // JSON parse of the WHOLE feed. Missing sizes are simply absent — the client falls back per key.
            Dictionary<string, string> urls = new(3);
            if (!string.IsNullOrEmpty(version.ImageUrl1x))
                urls["1"] = version.ImageUrl1x;
            if (!string.IsNullOrEmpty(version.ImageUrl2x))
                urls["2"] = version.ImageUrl2x;
            if (!string.IsNullOrEmpty(version.ImageUrl4x))
                urls["4"] = version.ImageUrl4x;
            index[(set.SetId, version.Id)] = urls;
        }
    }
}
