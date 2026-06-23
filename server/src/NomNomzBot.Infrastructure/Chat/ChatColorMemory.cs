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

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Cache-backed implementation of the per-channel chat-colour memory (chat-decoration spec §3.1). A chatter's colour is
/// stored under <c>chat:usercolor:{broadcasterId}:{userId}</c> with a rolling TTL, so the memory naturally forgets users
/// who have gone quiet and stays small.
/// </summary>
public sealed class ChatColorMemory : IChatColorMemory
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(2);

    private readonly ICacheService _cache;

    public ChatColorMemory(ICacheService cache)
    {
        _cache = cache;
    }

    public Task RememberAsync(
        Guid broadcasterId,
        string userId,
        string? colorHex,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrEmpty(colorHex) || string.IsNullOrEmpty(userId))
            return Task.CompletedTask;

        return _cache.SetAsync(Key(broadcasterId, userId), colorHex, Ttl, ct);
    }

    public Task<string?> GetAsync(
        Guid broadcasterId,
        string userId,
        CancellationToken ct = default
    ) => _cache.GetAsync<string>(Key(broadcasterId, userId), ct);

    private static string Key(Guid broadcasterId, string userId) =>
        $"chat:usercolor:{broadcasterId}:{userId}";
}
