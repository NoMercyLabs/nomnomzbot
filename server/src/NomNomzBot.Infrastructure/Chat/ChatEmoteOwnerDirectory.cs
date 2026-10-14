// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Names the Twitch channels that own emotes: login, display name and profile image. It reads the owner cache the
/// emote warmer fills (<see cref="ChatEmoteCacheKeys.TwitchOwner"/>) and looks up only the misses, all of them in
/// batched Get Users calls (100 ids per call), never one call per emote. A failed lookup leaves that owner out, so
/// the caller keeps the bare id.
/// </summary>
public sealed class ChatEmoteOwnerDirectory
{
    private const int GetUsersBatchSize = 100;
    private static readonly TimeSpan OwnerTtl = TimeSpan.FromHours(24);

    private readonly ICacheService _cache;
    private readonly ITwitchUsersApi _users;
    private readonly ILogger<ChatEmoteOwnerDirectory> _logger;

    public ChatEmoteOwnerDirectory(
        ICacheService cache,
        ITwitchUsersApi users,
        ILogger<ChatEmoteOwnerDirectory> logger
    )
    {
        _cache = cache;
        _users = users;
        _logger = logger;
    }

    /// <summary>Returns the known owners keyed by Twitch user id. An id that could not be resolved is absent.</summary>
    public async Task<IReadOnlyDictionary<string, ChatEmoteOwner>> ResolveAsync(
        IEnumerable<string> twitchUserIds,
        CancellationToken ct = default
    )
    {
        Dictionary<string, ChatEmoteOwner> owners = new(StringComparer.Ordinal);
        List<string> misses = [];

        foreach (string id in twitchUserIds.Distinct(StringComparer.Ordinal))
        {
            ChatEmoteOwner? cached = await _cache.GetAsync<ChatEmoteOwner>(
                ChatEmoteCacheKeys.TwitchOwner(id),
                ct
            );
            if (cached is not null)
                owners[id] = cached;
            else
                misses.Add(id);
        }

        foreach (string[] batch in misses.Chunk(GetUsersBatchSize))
            await LookUpBatchAsync(batch, owners, ct);

        return owners;
    }

    private async Task LookUpBatchAsync(
        string[] ids,
        Dictionary<string, ChatEmoteOwner> owners,
        CancellationToken ct
    )
    {
        Result<IReadOnlyList<TwitchUser>> found = await _users.GetUsersByIdsAsync(ids, ct);
        if (found.IsFailure)
        {
            _logger.LogDebug(
                "Twitch emote owners ({Count}) could not be looked up: {Error}",
                ids.Length,
                found.ErrorMessage
            );
            return;
        }

        foreach (TwitchUser user in found.Value)
        {
            ChatEmoteOwner owner = new(
                user.Id,
                user.Login,
                user.DisplayName,
                $"https://www.twitch.tv/{user.Login}",
                string.IsNullOrEmpty(user.ProfileImageUrl) ? null : user.ProfileImageUrl
            );
            owners[user.Id] = owner;
            await _cache.SetAsync(ChatEmoteCacheKeys.TwitchOwner(user.Id), owner, OwnerTtl, ct);
        }
    }
}
