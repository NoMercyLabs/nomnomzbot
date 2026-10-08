// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// Reads a viewer's follow from Helix (<c>channels/followers?user_id=</c>) through <see cref="FollowStateCache"/>.
/// Kick and YouTube have no follow-date API, so they are Unknown without a call.
/// </summary>
public sealed class FollowStateService : IFollowStateService
{
    private readonly ITwitchChannelsApi _channels;
    private readonly FollowStateCache _cache;

    public FollowStateService(ITwitchChannelsApi channels, FollowStateCache cache)
    {
        _channels = channels;
        _cache = cache;
    }

    public async Task<FollowLookup> ResolveAsync(
        Guid broadcasterId,
        string provider,
        string platformUserId,
        CancellationToken ct = default
    )
    {
        if (provider != AuthEnums.Platform.Twitch || string.IsNullOrWhiteSpace(platformUserId))
            return FollowLookup.Unknown;

        if (_cache.TryGet(broadcasterId, platformUserId, out FollowLookup cached))
            return cached;

        if (!_cache.TryBeginLookup(broadcasterId))
            return FollowLookup.Unknown;

        Result<TwitchChannelFollower?> response;
        try
        {
            response = await _channels.GetChannelFollowerAsync(broadcasterId, platformUserId, ct);
        }
        finally
        {
            _cache.EndLookup();
        }

        // A failure (missing scope, Helix down) is "could not find out" — never "not following".
        FollowLookup lookup =
            response.IsFailure ? FollowLookup.Unknown
            : response.Value is null ? new FollowLookup(FollowState.NotFollowing, null)
            : new FollowLookup(FollowState.Following, response.Value.FollowedAt);

        _cache.Set(broadcasterId, platformUserId, lookup);
        return lookup;
    }
}
